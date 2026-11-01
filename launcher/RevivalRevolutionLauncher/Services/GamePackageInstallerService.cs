namespace RevivalRevolution.Services;

/// <summary>
/// Package installation is intentionally fail-closed until the publisher supplies
/// independently trusted SHA-256 pins for each distribution archive.
/// A checksum manifest shipped inside the same archive cannot authenticate that archive.
/// </summary>
public sealed class GamePackageInstallerService
{
    public Task InstallAsync(string root, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Instalação automática bloqueada por segurança: ainda não há hashes SHA-256 " +
            "dos pacotes publicados, obtidos por um canal independente e confiável. " +
            "Nenhum arquivo do jogo foi extraído ou sobrescrito. A instalação existente " +
            "continua disponível; configure os hashes oficiais antes de habilitar downloads.");
    }

    public Task<IReadOnlyList<IntegrityResult>> VerifyAsync(string root, CancellationToken token)
        => Task.FromException<IReadOnlyList<IntegrityResult>>(
            new InvalidOperationException("A verificação da distribuição exige SHA256SUMS.json na raiz."));

    public Task RepairAsync(string root, IReadOnlyList<IntegrityResult> issues, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Reparo automático bloqueado: sem hashes autenticados dos pacotes, não é seguro " +
            "substituir arquivos da instalação.");
    }
}
