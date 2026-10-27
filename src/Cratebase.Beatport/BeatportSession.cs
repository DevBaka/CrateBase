using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cratebase.Beatport;

/// <summary>Cookie login → OAuth code → tokens. Credentials and tokens are never persisted.</summary>
public sealed class BeatportSession : IDisposable
{
    private static readonly Uri Origin = new("https://api.beatport.com/");
    private const string Redirect = "https://api.beatport.com/v4/auth/o/post-message/";
    private readonly HttpClient http;
    private readonly SemaphoreSlim refreshLock = new(1);
    private string clientId = "", accessToken = "", refreshToken = "";
    private DateTimeOffset expiresAt;

    private BeatportSession(HttpMessageHandler? handler)
    {
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() });
        http.BaseAddress = Origin; http.Timeout = TimeSpan.FromSeconds(45);
    }
    public static async Task<BeatportSession> LoginAsync(string username, string password, CancellationToken ct, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) throw new InvalidOperationException("Benutzername und Passwort eingeben.");
        var session = new BeatportSession(handler);
        try { await session.LoginCore(username.Trim(), password, ct); return session; }
        catch { session.Dispose(); throw; }
    }
    private async Task<string> GetText(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        Ensure(response, "Anmeldedaten laden"); return await response.Content.ReadAsStringAsync(ct);
    }
    private static string? FindClientId(string text)
    {
        var m = Regex.Match(text, "[\"']?API_CLIENT_ID[\"']?\\s*[:=]\\s*[\"']([^\"']+)[\"']", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        return m.Success ? m.Groups[1].Value : null;
    }
    private async Task LoginCore(string username, string password, CancellationToken ct)
    {
        // Resolve the public docs client from Beatport itself rather than hardcoding a borrowed secret.
        var html = await GetText("v4/docs/", ct);
        string? resolved = FindClientId(html);
        foreach (Match script in Regex.Matches(html, "<script\\b[^>]*\\bsrc\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
        {
            if (resolved is not null) break;
            if (!Uri.TryCreate(Origin, WebUtility.HtmlDecode(script.Groups[1].Value), out var uri) || uri.GetLeftPart(UriPartial.Authority) != Origin.GetLeftPart(UriPartial.Authority)) continue;
            resolved = FindClientId(await GetText(uri.AbsoluteUri, ct));
        }
        clientId = resolved ?? throw new InvalidOperationException("Beatport-Anmeldeseite hat sich geändert: Client-Konfiguration nicht gefunden.");
        using (var login = await http.PostAsJsonAsync("v4/auth/login/", new { username, password }, ct)) Ensure(login, "Login");
        var state = Guid.NewGuid().ToString("N");
        var query = new Dictionary<string, string> { ["response_type"] = "code", ["client_id"] = clientId, ["redirect_uri"] = Redirect, ["state"] = state };
        using var authorize = await http.GetAsync("v4/auth/o/authorize/?" + Query(query), ct);
        if (authorize.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther)
            || authorize.Headers.Location is not Uri location)
            throw new InvalidOperationException("Beatport hat die Anmeldung nicht freigegeben. Möglicherweise ist eine zusätzliche Browser-Bestätigung erforderlich.");
        var redirect = location.IsAbsoluteUri ? location : new Uri(Origin, location);
        if (redirect.GetLeftPart(UriPartial.Path) != Redirect) throw new InvalidOperationException("Beatport lieferte ein unerwartetes Anmeldeziel.");
        var values = ParseQuery(redirect.Query);
        if (!values.TryGetValue("state", out var returnedState) || returnedState != state) throw new InvalidOperationException("Beatport-Anmeldung konnte nicht sicher zugeordnet werden.");
        if (!values.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Beatport hat keinen Anmeldecode ausgegeben.");
        await Exchange(new() { ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = clientId, ["redirect_uri"] = Redirect }, ct);
    }
    private static string Query(Dictionary<string,string> values) => string.Join('&', values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    private static Dictionary<string,string> ParseQuery(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=', 2)).ToDictionary(p => WebUtility.UrlDecode(p[0]), p => p.Length == 2 ? WebUtility.UrlDecode(p[1]) : "");
    private async Task Exchange(Dictionary<string,string> fields, CancellationToken ct)
    {
        using var body = new FormUrlEncodedContent(fields);
        using var response = await http.PostAsync("v4/auth/o/token/", body, ct);
        Ensure(response, "Sitzung erneuern");
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); var data = doc.RootElement;
            var access = data.GetProperty("access_token").GetString();
            if (string.IsNullOrWhiteSpace(access)) throw new JsonException();
            var refreshed = data.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
            var seconds = data.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var n) ? n : 3600;
            accessToken = access; refreshToken = refreshed ?? refreshToken;
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, seconds - Math.Min(60, seconds / 10)));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new InvalidOperationException("Beatport lieferte eine unbekannte Sitzungsantwort. Bitte erneut anmelden."); }
    }
    public async Task<string> AccessTokenAsync(CancellationToken ct, bool forceRefresh = false)
    {
        await refreshLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && accessToken.Length > 0 && DateTimeOffset.UtcNow < expiresAt) return accessToken;
            if (string.IsNullOrEmpty(refreshToken)) throw new InvalidOperationException("Beatport-Sitzung abgelaufen. Bitte erneut anmelden.");
            await Exchange(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = clientId }, ct);
            return accessToken;
        }
        finally { refreshLock.Release(); }
    }
    private static void Ensure(HttpResponseMessage r, string step)
    {
        if (r.IsSuccessStatusCode) return;
        // Never surface response bodies: they may echo credentials, codes or tokens.
        throw new InvalidOperationException($"Beatport {step} ({(int)r.StatusCode}): " + ((int)r.StatusCode switch
        {
            400 or 401 => "Anmeldedaten oder Sitzung nicht akzeptiert. Bitte erneut anmelden.",
            403 => "Zugriff abgelehnt. Zusätzliche Bestätigung oder Browser-Anmeldung kann erforderlich sein.",
            429 => "Zu viele Anfragen. Bitte später erneut versuchen.",
            _ => "Anmeldung derzeit nicht möglich. Bitte später erneut versuchen."
        }));
    }
    public void Dispose() { accessToken = ""; refreshToken = ""; http.Dispose(); }
}
