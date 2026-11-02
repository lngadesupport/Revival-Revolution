using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace RevivalRevolution.Services;

/// <summary>
/// Stores Ranked credentials in the current Windows user's DPAPI scope.
/// The bearer token is never written to disk in plaintext.
/// </summary>
public sealed class RankedCredentialStore
{
    private sealed class StoredCredentials
    {
        public string ServerUrl { get; set; } = "";
        public string PlayerId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ProtectedToken { get; set; } = "";
    }

    public sealed record Credentials(string ServerUrl, string PlayerId, string DisplayName, string Token);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description,
        IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description,
        IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevivalRevolution", "Ranked");
    private static string FilePath => Path.Combine(DirectoryPath, "credentials.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Credentials? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var stored = JsonSerializer.Deserialize<StoredCredentials>(File.ReadAllText(FilePath));
            if (stored is null || string.IsNullOrWhiteSpace(stored.ProtectedToken)) return null;
            var token = Unprotect(Convert.FromBase64String(stored.ProtectedToken));
            return new Credentials(stored.ServerUrl, stored.PlayerId, stored.DisplayName, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or CryptographicException)
        {
            return null;
        }
    }

    public bool HasStoredToken()
    {
        var stored = ReadStored();
        return stored is not null && !string.IsNullOrWhiteSpace(stored.ProtectedToken);
    }

    public string LoadServerUrl()
    {
        try
        {
            if (!File.Exists(FilePath)) return "";
            return JsonSerializer.Deserialize<StoredCredentials>(File.ReadAllText(FilePath))?.ServerUrl ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return "";
        }
    }

    public void SaveServerUrl(string serverUrl)
    {
        var existing = ReadStored();
        Write(new StoredCredentials
        {
            ServerUrl = serverUrl,
            PlayerId = existing?.PlayerId ?? "",
            DisplayName = existing?.DisplayName ?? "",
            ProtectedToken = existing?.ProtectedToken ?? ""
        });
    }

    public void Save(string serverUrl, string playerId, string displayName, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token vazio.", nameof(token));
        Write(new StoredCredentials
        {
            ServerUrl = serverUrl,
            PlayerId = playerId,
            DisplayName = displayName,
            ProtectedToken = Convert.ToBase64String(Protect(token))
        });
    }

    private static StoredCredentials? ReadStored()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<StoredCredentials>(File.ReadAllText(FilePath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void Write(StoredCredentials credentials)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(credentials, JsonOptions));
        File.Move(temp, FilePath, true);
    }

    private static byte[] Protect(string value) => Transform(System.Text.Encoding.UTF8.GetBytes(value), protect: true);
    private static string Unprotect(byte[] value) => System.Text.Encoding.UTF8.GetString(Transform(value, protect: false));

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var inputPtr = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, inputPtr, bytes.Length);
            var input = new DataBlob { Length = bytes.Length, Data = inputPtr };
            DataBlob output;
            var success = protect
                ? CryptProtectData(ref input, "Revival Revolution Ranked", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new CryptographicException(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, output.Length);
                return result;
            }
            finally
            {
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inputPtr);
        }
    }
}
