using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevivalRevolution.Services;

public sealed class RankedApiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private string? _endpoint;
    private string? _token;

    public void Configure(string endpoint, string? token)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _token = token;
    }

    public Task<JsonObject> GetAsync(string path, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, path, null, true, cancellationToken);

    public Task<JsonObject> PostAsync(string path, JsonObject body, bool authenticated = true,
        CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, path, body, authenticated, cancellationToken);

    private async Task<JsonObject> SendAsync(HttpMethod method, string path, JsonObject? body,
        bool authenticated, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_endpoint))
            throw new InvalidOperationException("Configure o endereço do servidor Ranked.");
        using var request = new HttpRequestMessage(method, new Uri(new Uri(_endpoint), path));
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
        if (authenticated)
        {
            if (string.IsNullOrWhiteSpace(_token))
                throw new InvalidOperationException("A conta ainda não está autenticada.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonObject payload;
        try { payload = JsonNode.Parse(text) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { payload = new JsonObject(); }
        if (!response.IsSuccessStatusCode)
        {
            var detail = payload["message"]?.GetValue<string>() ?? payload["error"]?.GetValue<string>()
                ?? response.ReasonPhrase ?? "erro HTTP";
            throw new HttpRequestException($"Servidor Ranked respondeu {(int)response.StatusCode}: {detail}");
        }
        return payload;
    }

    public static string ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback ||
                uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IsRadminVpnAddress(uri.Host))))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Use HTTPS ou HTTP pela Radmin VPN (IP virtual 26.x.x.x). HTTP público não é permitido.");
        return uri.ToString().TrimEnd('/');
    }

    private static bool IsRadminVpnAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            return false;
        return address.GetAddressBytes()[0] == 26;
    }

    public static string? LoadEndpoint()
    {
        try { return File.Exists(EndpointPath) ? File.ReadAllText(EndpointPath, Encoding.UTF8).Trim() : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static void SaveEndpoint(string endpoint)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EndpointPath)!);
        File.WriteAllText(EndpointPath, ValidateEndpoint(endpoint), Encoding.UTF8);
    }

    private static string EndpointPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevivalRevolution", "ranked-endpoint.txt");
}

public sealed class RankedCredentialStore
{
    private const uint Generic = 1;
    private const uint LocalMachine = 2;

    public string? Read(string endpoint)
    {
        var target = Target(endpoint);
        if (!CredRead(target, Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return null;
            throw new InvalidOperationException($"Windows Credential Manager não pôde ler a credencial ({error}).");
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return null;
            return Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
        }
        finally { CredFree(pointer); }
    }

    public void Write(string endpoint, string token)
    {
        var target = Target(endpoint);
        var blob = Encoding.Unicode.GetBytes(token);
        var blobPointer = Marshal.AllocHGlobal(blob.Length);
        var targetPointer = Marshal.StringToCoTaskMemUni(target);
        var userPointer = Marshal.StringToCoTaskMemUni("Revival Revolution Ranked");
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new NativeCredential
            {
                Type = Generic, TargetName = targetPointer, CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer, Persist = LocalMachine, UserName = userPointer
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException($"Windows Credential Manager não salvou o token ({Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            Marshal.FreeHGlobal(blobPointer);
            Marshal.FreeCoTaskMem(targetPointer);
            Marshal.FreeCoTaskMem(userPointer);
        }
    }

    private static string Target(string endpoint)
    {
        var uri = new Uri(RankedApiClient.ValidateEndpoint(endpoint));
        return $"RevivalRevolution:Ranked:{uri.Scheme}://{uri.Authority}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
}