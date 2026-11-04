using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RevivalRevolution.Services;

public sealed record LauncherUpdateInfo(
    string Version,
    string PackageUrl,
    string Sha256,
    string ReleaseNotes,
    IReadOnlyList<string> Files,
    IReadOnlyDictionary<string, string> FileHashes,
    bool IsRepair);

public sealed record UpdateOperationProgress(string Stage, string Detail);

public sealed class LauncherUpdateService
{
    private const long MaxPackageBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxEntries = 50_000;
    private static readonly HttpClient ManifestHttp = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly HashSet<string> TrustedReleaseHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com", "release-assets.githubusercontent.com"
    };
    private static readonly DownloadService Downloads = new();
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };
    private static readonly HashSet<string> ProtectedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "launcher-update.json", "launcher-config.json", "python-runtime.txt", "autopunch-config.json",
        "Launcher/apply-update.cmd", "Launcher/apply-update.ps1", "MetalNetplayBridge.cs", "MetalOfflineBridgeImGui.cs", "SHA256SUMS.json"
    };

    public async Task<LauncherUpdateInfo?> CheckAsync(string root, CancellationToken token = default)
    {
        var config = Path.Combine(root, "launcher-update.json");
        if (!File.Exists(config)) return null;

        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(config, token));
        var manifestUrl = settings.RootElement.GetProperty("manifestUrl").GetString();
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Equals("/lngadesupport/Revival-Revolution/releases/latest/download/launcher-update.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O manifesto deve vir da Release mais recente do repositório oficial Revival-Revolution no GitHub.");

        using var response = await GetManifestAsync(uri, token);
        if (response is null) return null; // No stable release has been published yet.
        var manifestBytes = await ReadBoundedManifestAsync(response.Content, token);
        using var manifest = JsonDocument.Parse(manifestBytes);
        var data = manifest.RootElement;
        var versionText = data.GetProperty("version").GetString() ?? "";
        if (!Version.TryParse(versionText, out var remoteVersion))
            throw new InvalidDataException("A versão remota do launcher não é válida.");
        // Git tags use MAJOR.MINOR.PATCH while .NET assemblies use four parts.
        // Normalize 0.8.1 to 0.8.1.0 so same-version repairs are not rejected.
        remoteVersion = new Version(remoteVersion.Major, remoteVersion.Minor,
            Math.Max(0, remoteVersion.Build), Math.Max(0, remoteVersion.Revision));

        var localVersion = typeof(LauncherUpdateService).Assembly.GetName().Version ?? new Version(0, 0);
        if (remoteVersion < localVersion) return null;

        var fileElement = data.GetProperty("files");
        if (fileElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("A lista de arquivos da atualização não é um array.");

        var files = fileElement.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                throw new InvalidDataException("A lista de arquivos contém um caminho vazio ou inválido.");
            var path = item.GetString()!;
            ValidateRelativePath(path);
            if (IsProtectedPath(path))
                throw new InvalidDataException($"O manifesto tenta substituir um arquivo protegido: {path}");
            return path;
        }).ToArray();

        if (files.Length == 0 || files.Length > MaxEntries ||
            files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            throw new InvalidDataException("A lista de arquivos da atualização está vazia ou contém duplicatas.");

        var hashesElement = data.GetProperty("fileHashes");
        if (hashesElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("O manifesto não contém hashes SHA-256 individuais dos arquivos.");
        var fileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in hashesElement.EnumerateObject())
        {
            ValidateRelativePath(property.Name);
            if (!files.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"O manifesto contém hash para arquivo não autorizado: {property.Name}");
            var hash = property.Value.GetString() ?? "";
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || !fileHashes.TryAdd(property.Name, hash))
                throw new InvalidDataException($"Hash SHA-256 individual inválido ou duplicado: {property.Name}");
        }
        if (fileHashes.Count != files.Length || files.Any(path => !fileHashes.ContainsKey(path)))
            throw new InvalidDataException("Todo arquivo autorizado precisa ter exatamente um hash SHA-256.");

        var changedFiles = await FindFilesNeedingUpdateAsync(root, files, fileHashes, token);
        if (changedFiles.Count == 0) return null;
        var isRepair = remoteVersion == localVersion;
        return new LauncherUpdateInfo(
            versionText,
            data.GetProperty("packageUrl").GetString() ?? "",
            data.GetProperty("sha256").GetString() ?? "",
            data.TryGetProperty("releaseNotes", out var notes) ? notes.GetString() ?? "" : "",
            changedFiles,
            fileHashes,
            isRepair);
    }

    public static async Task<IReadOnlyList<string>> FindFilesNeedingUpdateAsync(
        string root,
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, string> expectedHashes,
        CancellationToken token = default)
    {
        var changedFiles = new List<string>();
        foreach (var relative in files)
        {
            token.ThrowIfCancellationRequested();
            ValidateRelativePath(relative);
            if (IsProtectedPath(relative))
                throw new InvalidDataException($"O atualizador não pode reparar este arquivo protegido: {relative}");
            if (!expectedHashes.TryGetValue(relative, out var expectedHash) ||
                expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
                throw new InvalidDataException($"Hash SHA-256 ausente ou inválido: {relative}");

            EnsureSafeDestination(root, relative);
            var localPath = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath))
            {
                changedFiles.Add(relative);
                continue;
            }

            await using var localStream = File.OpenRead(localPath);
            var localHash = Convert.ToHexString(await SHA256.HashDataAsync(localStream, token));
            if (!localHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                changedFiles.Add(relative);
        }
        return changedFiles;
    }

    public async Task<string> DownloadAndStageAsync(
        string root,
        LauncherUpdateInfo update,
        IProgress<DownloadProgress>? progress,
        CancellationToken token,
        IProgress<UpdateOperationProgress>? stageProgress = null)
    {
        if (!Uri.TryCreate(update.PackageUrl, UriKind.Absolute, out var packageUri) ||
            packageUri.Scheme != Uri.UriSchemeHttps ||
            !packageUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !packageUri.AbsolutePath.Equals("/lngadesupport/Revival-Revolution/releases/latest/download/RevivalRevolution-win-x64.zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O pacote deve vir da Release mais recente do repositório oficial Revival-Revolution no GitHub.");
        if (update.Sha256.Length != 64 || !update.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("SHA-256 do pacote inválido.");

        EnsureSafeDirectoryPath(root, "Launcher/Downloads");
        EnsureSafeDirectoryPath(root, "Launcher/UpdateStaging");
        EnsureSafeDirectoryPath(root, "Launcher/UpdateBackup");
        EnsureSafeDirectoryPath(root, "Launcher/Logs");
        var launcherDirectory = Path.Combine(root, "Launcher");
        var downloadsDirectory = Path.Combine(launcherDirectory, "Downloads");
        var staging = Path.Combine(launcherDirectory, "UpdateStaging");
        Directory.CreateDirectory(downloadsDirectory);

        var zip = Path.Combine(downloadsDirectory, "launcher-update.zip");
        stageProgress?.Report(new("download", "Baixando pacote oficial do GitHub..."));
        await Downloads.DownloadAsync(packageUri.ToString(), zip, progress, token, TrustedReleaseHosts);

        var packageLength = new FileInfo(zip).Length;
        if (packageLength <= 0 || packageLength > MaxPackageBytes)
            throw new InvalidDataException("O pacote de atualização está vazio ou excede o limite de 2 GiB.");

        stageProgress?.Report(new("verify", "Conferindo a integridade SHA-256 do pacote..."));
        await VerifyPackageHashAsync(zip, update.Sha256, token);

        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        try
        {
            stageProgress?.Report(new("extract", "Validando o manifesto e preparando os arquivos autorizados..."));
            ExtractAuthorizedFiles(zip, staging, update.Files, update.FileHashes, token);
            stageProgress?.Report(new("finalize", "Preparando a aplicação segura da atualização..."));
            return await CreateApplyScriptAsync(root, staging, update.Files, update.FileHashes, token);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            throw;
        }
    }

    private static async Task<byte[]> ReadBoundedManifestAsync(HttpContent content, CancellationToken token)
    {
        const int maxBytes = 1_048_576;
        if (content.Headers.ContentLength is > maxBytes)
            throw new InvalidDataException("O manifesto de atualização excede 1 MB.");

        await using var input = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (output.Length + read > maxBytes)
                throw new InvalidDataException("O manifesto de atualização excede 1 MB.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return output.ToArray();
    }

    private static async Task<HttpResponseMessage?> GetManifestAsync(Uri uri, CancellationToken token)
    {
        var response = await ManifestHttp.GetAsync(uri, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || finalUri.Scheme != Uri.UriSchemeHttps ||
            !TrustedReleaseHosts.Contains(finalUri.Host))
        {
            response.Dispose();
            throw new InvalidOperationException("O manifesto foi redirecionado para um host que não pertence ao GitHub Releases oficial.");
        }

        try { response.EnsureSuccessStatusCode(); return response; }
        catch { response.Dispose(); throw; }
    }

    private static async Task VerifyPackageHashAsync(string zip, string expectedHash, CancellationToken token)
    {
        await using var stream = File.OpenRead(zip);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Hash SHA-256 do pacote de atualização não confere.");
    }

    private static void ExtractAuthorizedFiles(
        string zip,
        string staging,
        IReadOnlyList<string> requestedFiles,
        IReadOnlyDictionary<string, string> expectedHashes,
        CancellationToken token)
    {
        var allowed = requestedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in allowed)
        {
            ValidateRelativePath(path);
            if (IsProtectedPath(path))
                throw new InvalidDataException($"O atualizador não pode substituir este arquivo protegido: {path}");
        }

        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count > MaxEntries ||
            archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).Sum(entry => entry.Length) > MaxPackageBytes)
            throw new InvalidDataException("O pacote de atualização excede os limites permitidos.");

        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefix = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            token.ThrowIfCancellationRequested();
            var relative = entry.FullName.Replace('\\', '/');
            ValidateRelativePath(relative);
            if (!allowed.Contains(relative)) continue;
            if (!extracted.Add(relative))
                throw new InvalidDataException($"Arquivo duplicado no pacote de atualização: {relative}");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException($"Links simbólicos não são permitidos: {relative}");

            var target = Path.GetFullPath(Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Caminho fora da staging: {relative}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, false);
        }

        var missing = allowed.Except(extracted, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("Arquivos listados no manifesto não encontrados no pacote: " + string.Join(", ", missing));

        // The package hash authenticates the ZIP as a whole; verify each extracted
        // payload against the manifest before it can be copied into the installation.
        VerifyStagedFiles(staging, requestedFiles, expectedHashes);
    }

    public static void VerifyStagedFiles(
        string staging,
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, string> expectedHashes)
    {
        var root = Path.GetFullPath(staging);
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A pasta de staging está ausente ou é um link/reparse point.");
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var relative in files)
        {
            ValidateRelativePath(relative);
            if (IsProtectedPath(relative))
                throw new InvalidDataException($"O atualizador não pode verificar um arquivo protegido: {relative}");
            if (!expectedHashes.TryGetValue(relative, out var expectedHash) ||
                expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
                throw new InvalidDataException($"Hash SHA-256 ausente ou inválido: {relative}");

            EnsureSafeDestination(root, relative);
            var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Arquivo fora da staging: {relative}");
            if (!File.Exists(target) || (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Arquivo extraído ausente ou inseguro: {relative}");

            using var stream = File.OpenRead(target);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Hash SHA-256 do arquivo extraído não confere: {relative}");
        }
    }

    private static async Task<string> CreateApplyScriptAsync(
        string root,
        string staging,
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, string> fileHashes,
        CancellationToken token)
    {
        var backup = Path.Combine(root, "Launcher", "UpdateBackup");
        var logs = Path.Combine(root, "Launcher", "Logs");
        var script = Path.Combine(root, "Launcher", "apply-update.ps1");
        var command = Path.Combine(root, "Launcher", "apply-update.cmd");

        EnsureSafeDestination(root, "Launcher/apply-update.ps1");
        EnsureSafeDestination(root, "Launcher/apply-update.cmd");
        EnsureSafeDirectoryPath(root, "Launcher/UpdateBackup");
        EnsureSafeDirectoryPath(root, "Launcher/Logs");
        Directory.CreateDirectory(logs);

        var ps = new StringBuilder();
        ps.AppendLine("$ErrorActionPreference = 'Stop'");
        ps.AppendLine("$launcherPid = " + Process.GetCurrentProcess().Id);
        ps.AppendLine("for ($i = 0; $i -lt 60 -and (Get-Process -Id $launcherPid -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 1 }");
        ps.AppendLine("if (Get-Process -Id $launcherPid -ErrorAction SilentlyContinue) { throw 'Launcher ainda está em execução.' }");
        ps.AppendLine($"$stage = {PsQuote(staging)}");
        ps.AppendLine($"$root = {PsQuote(root)}");
        ps.AppendLine($"$backup = {PsQuote(backup)}");
        ps.AppendLine($"$log = {PsQuote(Path.Combine(logs, "update-error.log"))}");
        ps.AppendLine("function Assert-NoReparsePath([string]$Path, [string]$Boundary) {");
        ps.AppendLine("  $full = [System.IO.Path]::GetFullPath($Path)");
        ps.AppendLine("  $base = [System.IO.Path]::GetFullPath($Boundary).TrimEnd([System.IO.Path]::DirectorySeparatorChar)");
        ps.AppendLine("  if (-not ($full.Equals($base, [System.StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($base + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))) { throw ('Path escapes update root: ' + $full) }");
        ps.AppendLine("  $cursor = $full");
        ps.AppendLine("  while ($cursor) {");
        ps.AppendLine("    if (Test-Path -LiteralPath $cursor) { $entry = Get-Item -LiteralPath $cursor -Force; if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Reparse point is not allowed: ' + $cursor) } }");
        ps.AppendLine("    if ($cursor.Equals($base, [System.StringComparison]::OrdinalIgnoreCase)) { break }");
        ps.AppendLine("    $cursor = Split-Path -Parent $cursor");
        ps.AppendLine("  }");
        ps.AppendLine("}");
        ps.AppendLine("$items = @(");
        for (var i = 0; i < files.Count; i++)
        {
            var relative = files[i];
            if (!fileHashes.TryGetValue(relative, out var hash) || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                throw new InvalidDataException($"Hash SHA-256 ausente ou inválido para {relative}.");
            EnsureSafeDestination(root, relative);
            var source = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
            var destination = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            var comma = i == files.Count - 1 ? "" : ",";
            ps.AppendLine($"  [pscustomobject]@{{ Relative = {PsQuote(relative)}; Source = {PsQuote(source)}; Destination = {PsQuote(destination)}; Sha256 = {PsQuote(hash.ToUpperInvariant())} }}{comma}");
        }
        ps.AppendLine(")");
        ps.AppendLine("$processed = [System.Collections.Generic.List[object]]::new()");
        ps.AppendLine("try {");
        ps.AppendLine("  Assert-NoReparsePath $root $root");
        ps.AppendLine("  Assert-NoReparsePath $stage $root");
        ps.AppendLine("  Assert-NoReparsePath $backup $root");
        ps.AppendLine("  Assert-NoReparsePath $log $root");
        ps.AppendLine("  if (-not (Test-Path -LiteralPath $stage -PathType Container)) { throw 'Update staging folder is missing.' }");
        ps.AppendLine("  foreach ($item in $items) {");
        ps.AppendLine("    Assert-NoReparsePath $item.Source $root");
        ps.AppendLine("    if (-not (Test-Path -LiteralPath $item.Source -PathType Leaf)) { throw ('Staged file is missing: ' + $item.Relative) }");
        ps.AppendLine("    if ((Get-FileHash -LiteralPath $item.Source -Algorithm SHA256).Hash -ne $item.Sha256) { throw ('Staged file hash mismatch: ' + $item.Relative) }");
        ps.AppendLine("    Assert-NoReparsePath $item.Destination $root");
        ps.AppendLine("    if (Test-Path -LiteralPath $item.Destination -PathType Container) { throw ('Destination is a directory: ' + $item.Relative) }");
        ps.AppendLine("  }");
        ps.AppendLine("  if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }");
        ps.AppendLine("  New-Item -ItemType Directory -Force -Path $backup | Out-Null");
        ps.AppendLine("  foreach ($item in $items) {");
        ps.AppendLine("    Assert-NoReparsePath $item.Source $root");
        ps.AppendLine("    if ((Get-FileHash -LiteralPath $item.Source -Algorithm SHA256).Hash -ne $item.Sha256) { throw ('Staged file changed before copy: ' + $item.Relative) }");
        ps.AppendLine("    Assert-NoReparsePath $item.Destination $root");
        ps.AppendLine("    $bak = Join-Path $backup $item.Relative");
        ps.AppendLine("    if (Test-Path -LiteralPath $item.Destination) { New-Item -ItemType Directory -Force -Path (Split-Path -Parent $bak) | Out-Null; Move-Item -LiteralPath $item.Destination -Destination $bak -Force }");
        ps.AppendLine("    $processed.Add($item)");
        ps.AppendLine("    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $item.Destination) | Out-Null");
        ps.AppendLine("    Copy-Item -LiteralPath $item.Source -Destination $item.Destination -Force");
        ps.AppendLine("    if ((Get-FileHash -LiteralPath $item.Destination -Algorithm SHA256).Hash -ne $item.Sha256) { throw ('Installed file hash mismatch: ' + $item.Relative) }");
        ps.AppendLine("  }");
        ps.AppendLine($"  $launched = Start-Process -FilePath {PsQuote(Path.Combine(root, "RevivalRevolution.exe"))} -WorkingDirectory $root -PassThru");
        ps.AppendLine("  Start-Sleep -Seconds 2");
        ps.AppendLine("  if ($launched.HasExited -and $launched.ExitCode -ne 0) { throw ('Updated launcher exited with code ' + $launched.ExitCode) }");
        ps.AppendLine("} catch {");
        ps.AppendLine("  $failure = $_.Exception.ToString()");
        ps.AppendLine("  foreach ($item in $processed.ToArray()) {");
        ps.AppendLine("    $dst = $item.Destination; $bak = Join-Path $backup $item.Relative");
        ps.AppendLine("    if (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Force }");
        ps.AppendLine("    if (Test-Path -LiteralPath $bak) { New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null; Move-Item -LiteralPath $bak -Destination $dst -Force }");
        ps.AppendLine("  }");
        ps.AppendLine("  Set-Content -LiteralPath $log -Value $failure -Encoding UTF8");
        ps.AppendLine("  throw");
        ps.AppendLine("}");
        ps.AppendLine("Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue");
        ps.AppendLine("Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue");

        var scriptTemp = script + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var commandTemp = command + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(scriptTemp, ps.ToString(), new UTF8Encoding(false), token);
            File.Move(scriptTemp, script, overwrite: true);
            var batch = "@echo off\r\ntimeout /t 2 /nobreak >nul\r\npowershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"%~dp0apply-update.ps1\"\r\n" +
                "if errorlevel 1 (echo Falha ao aplicar atualizacao. Consulte Logs\\update-error.log & exit /b 1)\r\ndel \"%~f0\"\r\ndel \"%~dp0apply-update.ps1\"\r\n";
            await File.WriteAllTextAsync(commandTemp, batch, new UTF8Encoding(false), token);
            File.Move(commandTemp, command, overwrite: true);
        }
        finally
        {
            if (File.Exists(scriptTemp)) File.Delete(scriptTemp);
            if (File.Exists(commandTemp)) File.Delete(commandTemp);
        }
        return command;
    }

    private static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";
    private static string CmdQuote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static void EnsureSafeDestination(string root, string relative)
    {
        EnsureSafeDirectoryPath(root, "");
        var current = Path.GetFullPath(root);
        var parts = relative.Replace('\\', '/').Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            current = Path.Combine(current, parts[i]);
            if (File.Exists(current))
                throw new InvalidDataException($"Um componente do destino não é uma pasta: {current}");
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"O destino contém um link/junction não permitido: {current}");
        }

        var target = Path.Combine(current, parts[^1]);
        if (Directory.Exists(target))
            throw new InvalidDataException($"O destino de arquivo já é uma pasta: {target}");
        if (File.Exists(target) &&
            (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"O arquivo de destino é um link/reparse point: {target}");
    }

    private static void EnsureSafeDirectoryPath(string root, string relative)
    {
        var current = Path.GetFullPath(root);
        if (!Directory.Exists(current) ||
            (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A pasta raiz do launcher está ausente ou é um link/reparse point.");

        if (string.IsNullOrEmpty(relative)) return;
        foreach (var part in relative.Replace('\\', '/').Split('/'))
        {
            current = Path.Combine(current, part);
            if (File.Exists(current))
                throw new InvalidDataException($"O caminho de atualização contém um arquivo onde deveria haver pasta: {current}");
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"O caminho de atualização contém um link/junction não permitido: {current}");
        }
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Caminho vazio no manifesto/pacote.");
        var parts = path.Replace('\\', '/').Split('/');
        if (path.StartsWith('/') || path.Contains(':') ||
            parts.Any(part => part.Length == 0 || part is "." or ".." ||
                part.EndsWith(' ') || part.EndsWith('.') ||
                part.Any(character => "<>\"|?*".Contains(character)) ||
                ReservedNames.Contains(part.Split('.')[0])))
            throw new InvalidDataException($"Caminho inseguro no pacote de atualização: {path}");
    }

    private static bool IsProtectedPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("Metal Revolution/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("ufo_Data/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Launcher/Downloads/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Launcher/Runtime/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Launcher/UpdateStaging/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Launcher/UpdateBackup/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Launcher/Config/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Launcher/Logs/", StringComparison.OrdinalIgnoreCase) ||
            // Community mods, imported mobile ports and user-created content are
            // user-owned data. Official launcher updates must never overwrite them.
            normalized.StartsWith("Launcher/Mods/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Launcher/UserContent/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Launcher/Content/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Launcher/Profiles/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Launcher/Backups/", StringComparison.OrdinalIgnoreCase) ||
            ProtectedFiles.Contains(normalized);
    }
}
