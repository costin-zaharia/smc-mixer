using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SmcMixer.HomeAssistant;

/// <summary>The few Home Assistant REST API calls the worker uses, authenticated with a long-lived access token.</summary>
public sealed class HomeAssistantClient : IHomeAssistant, IDisposable
{
    private readonly HttpClient _http;

    public HomeAssistantClient(IOptions<HomeAssistantOptions> options)
    {
        var o = options.Value;
        string tokenPath = o.TokenFile.StartsWith('~')
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), o.TokenFile.TrimStart('~', '/'))
            : o.TokenFile;
        if (!File.Exists(tokenPath))
            throw new FileNotFoundException($"Home Assistant token file not found: {tokenPath}");

        _http = new HttpClient { BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", File.ReadAllText(tokenPath).Trim());
    }

    public async Task CallServiceAsync(string domain, string service, object data, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync($"api/services/{domain}/{service}", data, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<LightInfo?> GetLightAsync(string entityId, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"api/states/{entityId}", ct);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("attributes", out var attributes)) return null;

        return new LightInfo(Int(attributes, "color_temp_kelvin"), Int(attributes, "min_color_temp_kelvin"), Int(attributes, "max_color_temp_kelvin"));

        static int? Int(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    }

    public void Dispose() => _http.Dispose();
}
