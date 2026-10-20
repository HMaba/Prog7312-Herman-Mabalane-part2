using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartxWPF.Services;

public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public string BaseUrl { get; private set; } = "http://localhost:5234";
    public bool HasToken => _http.DefaultRequestHeaders.Authorization is not null;

    public void Configure(string baseUrl, string? bearerToken)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Enter a valid API URL, such as http://localhost:5234.", nameof(baseUrl));
        BaseUrl = uri.ToString().TrimEnd('/');
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(bearerToken)
            ? null : new AuthenticationHeaderValue("Bearer", bearerToken.Trim());
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(Url(path), ct);
        return await ReadAsync<T>(response, ct);
    }
    public async Task<TOut> PostJsonAsync<TIn, TOut>(string path, TIn payload, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(Url(path), payload, _json, ct);
        return await ReadAsync<TOut>(response, ct);
    }
    public async Task<T> PostEmptyAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync(Url(path), null, ct);
        return await ReadAsync<T>(response, ct);
    }
    public async Task<T> UploadFileAsync<T>(string path, string filePath, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", Path.GetFileName(filePath));
        using var response = await _http.PostAsync(Url(path), form, ct);
        return await ReadAsync<T>(response, ct);
    }
    private string Url(string path) => $"{BaseUrl}/{path.TrimStart('/')}";
    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"API returned {(int)response.StatusCode} ({response.ReasonPhrase}). {body}");
        if (typeof(T) == typeof(string)) return (T)(object)body;
        return JsonSerializer.Deserialize<T>(body, _json) ?? throw new InvalidOperationException("The API returned an empty response.");
    }
    public void Dispose() => _http.Dispose();
}
