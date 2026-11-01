using System.Security.Cryptography;
using System.Text.Json;

namespace RevivalRevolution.Services;

public sealed record IntegrityResult(string Path, string ExpectedHash, string? ActualHash, string Status)
{
    public bool IsValid => Status == "OK";
}

public sealed class IntegrityService
{
    public async Task<IReadOnlyList<IntegrityResult>> VerifyAsync(string root, string manifestPath, CancellationToken token = default)
    {
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("Manifesto SHA-256 não encontrado.", manifestPath);
        var hashes = ParseManifest(await File.ReadAllTextAsync(manifestPath, token));
        var results = new List<IntegrityResult>();
        foreach (var item in hashes)
        {
            token.ThrowIfCancellationRequested();
            var relative = item.Key.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(root, relative));
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Caminho inválido no manifesto: {item.Key}");
            if (!File.Exists(full)) { results.Add(new(item.Key, item.Value, null, "AUSENTE")); continue; }
            await using var stream = File.OpenRead(full);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
            results.Add(new(item.Key, item.Value, actual, actual.Equals(item.Value, StringComparison.OrdinalIgnoreCase) ? "OK" : "ALTERADO"));
        }
        return results;
    }

    private static Dictionary<string,string> ParseManifest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path, string? hash)
        {
            if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(hash) &&
                hash.Length == 64 && hash.All(Uri.IsHexDigit))
                result[path.Replace('\\','/')] = hash.ToLowerInvariant();
        }
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                if ((e.TryGetProperty("path",out var p) || e.TryGetProperty("file",out p) || e.TryGetProperty("name",out p)) &&
                    (e.TryGetProperty("sha256",out var h) || e.TryGetProperty("hash",out h) || e.TryGetProperty("checksum",out h)))
                    Add(p.GetString(),h.GetString());
                else foreach (var prop in e.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String) Add(prop.Name,prop.Value.GetString());
                    else if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) Walk(prop.Value);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var child in e.EnumerateArray()) Walk(child);
        }
        Walk(doc.RootElement);
        if (result.Count == 0) throw new InvalidDataException("Formato do manifesto não reconhecido ou sem hashes SHA-256 válidos.");
        return result;
    }
}
