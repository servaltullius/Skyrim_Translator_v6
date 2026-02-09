using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.NexusMods;

public sealed class NexusModsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly string _applicationName;
    private readonly string _applicationVersion;

    public NexusModsClient(HttpClient httpClient, string applicationName, string applicationVersion)
    {
        _httpClient = httpClient;
        _applicationName = applicationName?.Trim() ?? "";
        _applicationVersion = applicationVersion?.Trim() ?? "";
    }

    public async Task<NexusMod> GetModAsync(string apiKey, string gameDomain, long modId, CancellationToken cancellationToken)
    {
        var url = BuildApiUrl($"v1/games/{Uri.EscapeDataString(gameDomain)}/mods/{modId}.json");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeaders(req, apiKey);

        using var resp = await _httpClient.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            throw new NexusModsHttpException((int)resp.StatusCode, $"GetMod failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}. {Truncate(body)}");
        }

        try
        {
            return JsonSerializer.Deserialize<NexusMod>(body, JsonOptions)
                   ?? throw new NexusModsException($"GetMod failed: invalid JSON. {Truncate(body)}");
        }
        catch (JsonException e)
        {
            throw new NexusModsException($"GetMod failed: invalid JSON. {Truncate(body)}", e);
        }
    }

    public async Task<string?> GetModDescriptionHtmlAsync(
        string apiKey,
        string gameDomain,
        long modId,
        CancellationToken cancellationToken
    )
    {
        var url = BuildApiUrl($"v1/games/{Uri.EscapeDataString(gameDomain)}/mods/{modId}/description.json");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeaders(req, apiKey);

        using var resp = await _httpClient.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            throw new NexusModsHttpException((int)resp.StatusCode, $"GetModDescription failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}. {Truncate(body)}");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("description", out var desc))
            {
                return desc.ValueKind == JsonValueKind.String ? desc.GetString() : desc.ToString();
            }

            // Some API variants may return the HTML directly as a string.
            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString();
            }

            return null;
        }
        catch (JsonException e)
        {
            throw new NexusModsException($"GetModDescription failed: invalid JSON. {Truncate(body)}", e);
        }
    }

    public async Task<IReadOnlyList<NexusModSearchResult>> SearchModsAsync(
        string apiKey,
        string gameDomain,
        string term,
        CancellationToken cancellationToken
    )
    {
        var url =
            BuildApiUrl(
                $"v1/games/{Uri.EscapeDataString(gameDomain)}/mods/search.json?term={Uri.EscapeDataString(term ?? "")}"
            );

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeaders(req, apiKey);

        using var resp = await _httpClient.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            throw new NexusModsHttpException((int)resp.StatusCode, $"SearchMods failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}. {Truncate(body)}");
        }

        try
        {
            return JsonSerializer.Deserialize<List<NexusModSearchResult>>(body, JsonOptions) ?? new List<NexusModSearchResult>();
        }
        catch (JsonException e)
        {
            throw new NexusModsException($"SearchMods failed: invalid JSON. {Truncate(body)}", e);
        }
    }

    private static Uri BuildApiUrl(string relativePath)
        => new($"https://api.nexusmods.com/{relativePath.TrimStart('/')}");

    private void AddHeaders(HttpRequestMessage req, string apiKey)
    {
        req.Headers.TryAddWithoutValidation("accept", "application/json");
        req.Headers.TryAddWithoutValidation("apikey", apiKey ?? "");

        if (!string.IsNullOrWhiteSpace(_applicationName))
        {
            req.Headers.TryAddWithoutValidation("Application-Name", _applicationName);
        }

        if (!string.IsNullOrWhiteSpace(_applicationVersion))
        {
            req.Headers.TryAddWithoutValidation("Application-Version", _applicationVersion);
        }
    }

    private static string Truncate(string s, int max = 500) => s.Length <= max ? s : s[..max];
}

public class NexusModsException : Exception
{
    public NexusModsException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class NexusModsHttpException : NexusModsException
{
    public NexusModsHttpException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
