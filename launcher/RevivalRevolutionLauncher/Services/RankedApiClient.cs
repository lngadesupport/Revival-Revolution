using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RevivalRevolution.Services;

public sealed record RankedProfile(string PlayerId, string DisplayName, int Rating);
public sealed record RankedMatch(string MatchId, string Status, string Region, int PlayerSlot,
    string OpponentName, int OpponentRating);

public sealed class RankedApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string? _token;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public RankedApiClient(string serverUrl, string? token = null)
    {
        if (!Uri.TryCreate(serverUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp)))
            throw new ArgumentException("Use uma URL HTTPS. HTTP só é permitido para localhost.", nameof(serverUrl));

        _baseUrl = uri.ToString().TrimEnd('/');
        _token = token;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<RankedProfile> RegisterAsync(string displayName, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Post, "/v1/register",
            new { display_name = displayName }, authenticated: false, cancellationToken);
        var root = document.RootElement;
        return new RankedProfile(
            RequiredString(root, "player_id"),
            RequiredString(root, "display_name"),
            root.GetProperty("rating").GetInt32());
    }

    public async Task<RankedProfile> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "/v1/me", null, true, cancellationToken);
        var root = document.RootElement;
        return new RankedProfile(
            RequiredString(root, "player_id"),
            RequiredString(root, "display_name"),
            root.GetProperty("rating").GetInt32());
    }

    public async Task<JsonElement> JoinQueueAsync(string region = "global", CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Post, "/v1/queue/join", new { region }, true, cancellationToken);
        return document.RootElement.Clone();
    }

    public async Task<JsonElement> GetQueueAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "/v1/queue", null, true, cancellationToken);
        return document.RootElement.Clone();
    }

    public async Task<RankedMatch?> GetMatchAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "/v1/match", null, true, cancellationToken);
        var match = document.RootElement.GetProperty("match");
        if (match.ValueKind == JsonValueKind.Null) return null;
        return new RankedMatch(
            RequiredString(match, "match_id"),
            RequiredString(match, "status"),
            RequiredString(match, "region"),
            match.GetProperty("player_slot").GetInt32(),
            RequiredString(match.GetProperty("opponent"), "display_name"),
            match.GetProperty("opponent").GetProperty("rating").GetInt32());
    }

    public async Task<bool> LeaveQueueAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Post, "/v1/queue/leave", new { }, true, cancellationToken);
        return document.RootElement.TryGetProperty("left", out var left) && left.GetBoolean();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        bool authenticated, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, _baseUrl + path);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        if (authenticated && string.IsNullOrWhiteSpace(_token))
            throw new InvalidOperationException("Esta identidade ainda não está autenticada.");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = TryReadError(text);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new RankedAuthenticationException("O servidor rejeitou a credencial salva. A identidade local foi preservada; não criei outra conta.");
            throw new HttpRequestException($"Servidor Ranked respondeu {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }
        try { return JsonDocument.Parse(text); }
        catch (JsonException ex) { throw new InvalidDataException("O servidor retornou JSON inválido.", ex); }
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Resposta do servidor sem o campo {name}.");
        return value.GetString()!;
    }

    private static string TryReadError(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("message", out var message)) return message.GetString() ?? "erro sem detalhe";
            if (doc.RootElement.TryGetProperty("error", out var error)) return error.GetString() ?? "erro sem detalhe";
        }
        catch (JsonException) { }
        return string.IsNullOrWhiteSpace(text) ? "resposta vazia" : text[..Math.Min(text.Length, 240)];
    }

    public void Dispose() => _http.Dispose();
}

public sealed class RankedAuthenticationException(string message) : Exception(message);
