using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Fam.Pulverentnahme.Web;

public sealed class OxaionRejectedException(string code, string title, string field, string reason)
    : Exception($"Oxaion rejected request: {code} {title}. Field: {field}. {reason}")
{
    public string Code { get; } = code;
    public string Field { get; } = field;
}

public sealed class OxaionTransportException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class OxaionClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OxaionOptions _options;
    private readonly ILogger<OxaionClient> _logger;

    public OxaionClient(IHttpClientFactory httpClientFactory, IOptions<OxaionOptions> options, ILogger<OxaionClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        ValidateConfiguration();
    }

    public async Task<OxaionSession> ConnectAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Password))
            throw new InvalidOperationException("Oxaion password is not configured. Set environment variable Oxaion__Password.");

        var client = _httpClientFactory.CreateClient(nameof(OxaionClient));
        client.Timeout = TimeSpan.FromSeconds(30);

        var query = new Dictionary<string, string>
        {
            ["user"] = _options.User,
            ["pwd"] = _options.Password,
            ["firm"] = _options.Firm
        };
        var url = $"{_options.ServerUrl.TrimEnd('/')}/app-tunnel/connect?{BuildQuery(query)}";

        string raw;
        int statusCode;
        string? contentType;
        try
        {
            using var response = await client.GetAsync(url, ct);
            statusCode = (int)response.StatusCode;
            contentType = response.Content.Headers.ContentType?.ToString();
            raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new OxaionTransportException($"Oxaion HTTP {statusCode} during CONNECT.");
        }
        catch (OxaionTransportException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new OxaionTransportException("Could not connect to oxaion.", ex);
        }

        var xml = ParseXml(raw, "CONNECT", statusCode, contentType);
        var error = xml.Descendants("ERROR").FirstOrDefault();
        if (error is not null)
            throw new InvalidOperationException($"Oxaion connect error: {error.Value.Trim()}");

        var sessionId = xml.Descendants("ID").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("Oxaion connect did not return a session ID.");

        return new OxaionSession(client, _options.ServerUrl.TrimEnd('/'), sessionId, _logger);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ServerUrl) || string.IsNullOrWhiteSpace(_options.User) || string.IsNullOrWhiteSpace(_options.Firm))
            throw new InvalidOperationException("Oxaion ServerUrl/User/Firm must be configured.");

        if (_options.StagingOnly)
        {
            if (_options.ServerUrl.Contains(":11108", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Safety stop: port 11108 is production and is blocked in STAGING prototype.");
            if (!_options.ServerUrl.Contains(":11118", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Safety stop: STAGING prototype expects oxaion port 11118.");
            if (!string.Equals(_options.Firm, "103", StringComparison.Ordinal))
                throw new InvalidOperationException("Safety stop: STAGING prototype expects company 103.");
        }
    }

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    internal static XDocument ParseXml(string raw, string context = "response", int? statusCode = null, string? contentType = null)
    {
        try
        {
            return XDocument.Parse(raw, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            var http = statusCode.HasValue ? $"HTTP {statusCode.Value}; " : "";
            var type = string.IsNullOrWhiteSpace(contentType) ? "" : $"Content-Type {contentType}; ";
            throw new InvalidOperationException(
                $"Oxaion {context} response was not valid XML ({http}{type}{DescribePayload(raw)}; XML parser line {ex.LineNumber}, position {ex.LinePosition}).",
                ex);
        }
    }

    private static string DescribePayload(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "empty response";

        var trimmed = raw.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        var kind = trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            ? "HTML response"
            : trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal)
                ? "JSON-like response"
                : trimmed.StartsWith("<", StringComparison.Ordinal)
                    ? "malformed XML response"
                    : "plain-text or binary-looking response";

        return $"{kind}, {raw.Length} chars";
    }
}

public sealed class OxaionSession : IAsyncDisposable
{
    private readonly HttpClient _client;
    private readonly string _serverUrl;
    private readonly ILogger _logger;
    private readonly string _actg = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + Random.Shared.Next(100000, 999999);
    private bool _disposed;

    public string SessionId { get; }

    internal OxaionSession(HttpClient client, string serverUrl, string sessionId, ILogger logger)
    {
        _client = client;
        _serverUrl = serverUrl;
        SessionId = sessionId;
        _logger = logger;
    }

    public async Task<OxaionCallResult> CallAsync(string program, string action, IReadOnlyDictionary<string, string>? dta, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["user"] = SessionId,
            ["pgmn"] = program,
            ["akto"] = action,
            ["actg"] = _actg,
            ["responseFormat"] = "xml"
        };

        if (dta is not null)
        {
            foreach (var pair in dta)
            {
                if (pair.Key is "user" or "pgmn" or "akto" or "actg" or "responseFormat") continue;
                form[pair.Key] = pair.Value ?? "";
            }
        }

        string raw;
        int statusCode;
        string? contentType;
        try
        {
            using var body = new FormUrlEncodedContent(form);
            using var response = await _client.PostAsync($"{_serverUrl}/app-tunnel/call", body, ct);
            statusCode = (int)response.StatusCode;
            contentType = response.Content.Headers.ContentType?.ToString();
            raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new OxaionTransportException($"Oxaion HTTP {statusCode} during {program} {action}.");
        }
        catch (OxaionTransportException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new OxaionTransportException($"Transport error during {program} {action}; booking outcome may be uncertain.", ex);
        }

        var xml = OxaionClient.ParseXml(raw, $"{program} {action}", statusCode, contentType);
        var error = xml.Descendants("ERROR").FirstOrDefault();
        if (error is not null)
            throw new InvalidOperationException($"Oxaion ERROR during {program} {action}: {error.Value.Trim()}");

        return new OxaionCallResult(raw, xml, ReadDta(xml));
    }

    public static void AssertNoFcod(OxaionCallResult result)
    {
        var fcod = result.Xml.Descendants("FCOD").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrWhiteSpace(fcod)) return;
        var title = result.Xml.Descendants("FTITLE").FirstOrDefault()?.Value.Trim() ?? "";
        var field = result.Xml.Descendants("FLDN").FirstOrDefault()?.Value.Trim() ?? "";
        var reason = result.Xml.Descendants("FREASON").FirstOrDefault()?.Value.Trim() ?? "";
        throw new OxaionRejectedException(fcod, title, field, reason);
    }

    public static Dictionary<string, string> ReadDta(XDocument xml)
    {
        var dta = xml.Descendants("DTA").FirstOrDefault();
        if (dta is null) return [];
        return dta.Elements().ToDictionary(x => x.Name.LocalName, x => x.Value ?? "", StringComparer.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            var url = $"{_serverUrl}/app-tunnel/disconnect?user={Uri.EscapeDataString(SessionId)}";
            await _client.GetAsync(url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Oxaion disconnect failed for session {SessionId}", SessionId);
        }
    }
}

public sealed record OxaionCallResult(string RawXml, XDocument Xml, Dictionary<string, string> Dta);
