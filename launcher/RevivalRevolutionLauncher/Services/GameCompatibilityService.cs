using System.Diagnostics;

namespace RevivalRevolution.Services;

public sealed record GameCompatibilityInfo(
    bool IsDetected,
    string VersionLabel,
    string Detail);

/// <summary>
/// Reads only stable installation metadata. Unknown game versions are reported, never rejected.
/// This keeps the launcher independent from future character/content patch identifiers.
/// </summary>
public sealed class GameCompatibilityService
{
    public GameCompatibilityInfo Inspect(string? gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
            return new(false, "JOGO NÃO DETECTADO", "Selecione a pasta da instalação.");

        var executable = Path.Combine(gameFolder, "ufo.exe");
        var dataDirectory = Path.Combine(gameFolder, "ufo_Data");
        if (!File.Exists(executable) || !Directory.Exists(dataDirectory))
            return new(false, "INSTALAÇÃO INCOMPLETA", "Esperados ufo.exe e ufo_Data.");

        try
        {
            var version = FileVersionInfo.GetVersionInfo(executable);
            var productVersion = FirstValue(version.ProductVersion, version.FileVersion);
            var build = string.IsNullOrWhiteSpace(productVersion)
                ? "versão não informada pelo executável"
                : $"versão {productVersion}";
            var managedDirectory = Path.Combine(dataDirectory, "Managed");
            var managedState = Directory.Exists(managedDirectory)
                ? "estrutura Managed detectada"
                : "estrutura Managed não identificada";

            return new(true, build, managedState);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(true, "VERSÃO NÃO IDENTIFICADA", "Arquivos principais detectados; metadados indisponíveis.");
        }
    }

    private static string? FirstValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
