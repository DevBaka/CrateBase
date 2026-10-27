using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace Cratebase.Beatport;
public sealed class BeatportClient : IBeatportCatalog, IDisposable
{
    private readonly HttpClient http;
    private readonly IProgress<string>? progress;
    private readonly BeatportSession? session;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public BeatportClient(string token, IProgress<string>? progress = null, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Access-Token fehlt.");
        http = handler is null ? new() : new(handler); http.BaseAddress = new("https://api.beatport.com/v4/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        http.Timeout = TimeSpan.FromSeconds(45); this.progress = progress;
    }
    public BeatportClient(BeatportSession session, IProgress<string>? progress = null, HttpMessageHandler? handler = null)
        : this("session", progress, handler) { this.session = session; http.DefaultRequestHeaders.Authorization = null; }
    private async Task<JsonElement> Request(string path, HttpMethod method, object? body, CancellationToken ct)
    {
        using var response = await Send(path, method, body, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && session is not null && method == HttpMethod.Get)
        {
            await session.AccessTokenAsync(ct, forceRefresh: true);
            using var retry = await Send(path, method, body, ct);
            return await Read(retry, ct);
        }
        return await Read(response, ct);
    }
    private async Task<HttpResponseMessage> Send(string path, HttpMethod method, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (session is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await session.AccessTokenAsync(ct));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, ct);
    }
    private static async Task<JsonElement> Read(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Beatport {(int)response.StatusCode}: " + ((int)response.StatusCode switch { 401 => "Token ungültig oder abgelaufen.", 403 => "Zugriff nicht freigegeben.", 429 => "Anfragelimit erreicht. Später erneut versuchen.", _ => "Anfrage fehlgeschlagen. API-Kompatibilität prüfen." }));
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default;
        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content)) return default;
        using var doc = JsonDocument.Parse(content); return doc.RootElement.Clone();
    }
    private async Task<IReadOnlyList<T>> All<T>(string path, CancellationToken ct)
    {
        var result = new List<T>(); var signatures = new HashSet<string>();
        for (var page = 1; ; page++)
        {
            var data = await Request($"{path}{(path.Contains('?') ? '&' : '?')}page={page}&per_page=100", HttpMethod.Get, null, ct);
            var rows = data.ValueKind == JsonValueKind.Array ? data : data.GetProperty("results");
            if (rows.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Unbekanntes Beatport-Seitenformat.");
            var signature = string.Join(',', rows.EnumerateArray().Select(r => r.GetProperty("id").ToString()));
            if (rows.GetArrayLength() > 0 && !signatures.Add(signature)) throw new InvalidOperationException("API wiederholt eine Seite. Import abgebrochen.");
            result.AddRange(rows.Deserialize<List<T>>(Json) ?? throw new InvalidOperationException("Leere API-Antwort."));
            progress?.Report($"{result.Count:N0} Einträge geladen · Seite {page}");
            bool next = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("next", out var n) && n.ValueKind is not (JsonValueKind.Null or JsonValueKind.False) && n.ToString().Length > 0;
            var total = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("count", out var count) && count.TryGetInt32(out var number) ? number : (int?)null;
            if (!next && total is not null && result.Count < total && rows.GetArrayLength() < 100)
                throw new InvalidOperationException("API-Seite unvollständig: Gesamtzahl noch nicht erreicht.");
            if (!next && (data.ValueKind == JsonValueKind.Array || (total is not null && result.Count >= total) || rows.GetArrayLength() < 100)) break;
            if (rows.GetArrayLength() == 0) throw new InvalidOperationException("Unvollständige Seitennavigation.");
            await Task.Delay(200, ct);
        }
        return result;
    }
    public async Task CheckAsync(CancellationToken ct) => await Request("my/account/", HttpMethod.Get, null, ct);
    public async Task<IReadOnlyList<NamedEntity>> ArtistsAsync(string query, CancellationToken ct)
    {
        var data = await Request($"catalog/search/?q={Uri.EscapeDataString(query)}&type=artists&per_page=100", HttpMethod.Get, null, ct);
        if (data.TryGetProperty("artists", out var artists)) data = artists;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("results", out var rows)) data = rows;
        return data.Deserialize<List<NamedEntity>>(Json) ?? throw new InvalidOperationException("Unbekanntes Artist-Suchformat.");
    }
    public Task<IReadOnlyList<NamedEntity>> GenresAsync(CancellationToken ct) => All<NamedEntity>("catalog/genres/", ct);
    public Task<IReadOnlyList<NamedEntity>> SubgenresAsync(long genre, CancellationToken ct) => All<NamedEntity>($"catalog/sub-genres/?genre_id={genre}", ct);
    public async Task<IReadOnlyList<Track>> TracksAsync(long? genre, long? subgenre, long? artist, CancellationToken ct)
    {
        if (artist is null && (genre is null || subgenre is null)) throw new ArgumentException("Genre und Subgenre auswählen.");
        var path = artist is not null ? $"catalog/artists/{artist}/tracks/?order_by=-publish_date" : $"catalog/tracks/?genre_id={genre}&sub_genre_id={subgenre}&order_by=-publish_date";
        var rows = await All<Track>(path, ct);
        if (artist is null && rows.Any(t => t.Subgenre?.Id != subgenre)) throw new InvalidOperationException("Subgenre-Filter anhand der API-Daten nicht verifizierbar. Kein Import.");
        return rows;
    }
    public Task<IReadOnlyList<NamedEntity>> PlaylistsAsync(CancellationToken ct) => All<NamedEntity>("my/playlists/", ct);
    public Task<IReadOnlyList<PlaylistItem>> PlaylistTracksAsync(long playlist, CancellationToken ct) => All<PlaylistItem>($"my/playlists/{playlist}/tracks/", ct);
    public async Task<NamedEntity> CreatePlaylistAsync(string name, CancellationToken ct) => (await Request("my/playlists/", HttpMethod.Post, new { name }, ct)).Deserialize<NamedEntity>(Json) ?? throw new InvalidOperationException("Playlist-Antwort fehlt.");
    public async Task AddAsync(long playlist, IEnumerable<long> tracks, CancellationToken ct) => await Request($"my/playlists/{playlist}/tracks/bulk/", HttpMethod.Post, new { track_ids = tracks.ToArray() }, ct);
    public async Task RemoveAsync(long playlist, long item, CancellationToken ct) => await Request($"my/playlists/{playlist}/tracks/{item}/", HttpMethod.Delete, null, ct);
    public void Dispose() { http.Dispose(); session?.Dispose(); }
}
