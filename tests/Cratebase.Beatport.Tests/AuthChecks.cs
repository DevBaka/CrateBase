using Cratebase.Beatport;
using System.Net;
using System.Text.Json;

internal static class AuthChecks
{
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
    private static HttpResponseMessage Json(object body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    public static async Task Run()
    {
        int loginRequests = 0, refreshes = 0, docsRequests = 0;
        string? state = null;
        var transport = new Handler(async (r, ct) =>
        {
            Check(r.RequestUri!.Host == "api.beatport.com", "Authentication stays on Beatport");
            switch (r.RequestUri.AbsolutePath)
            {
                case "/v4/docs/":
                    docsRequests++;
                    return new(HttpStatusCode.OK) { Content = new StringContent("<script src='https://untrusted.invalid/x.js'></script><script src='/static/client.js'></script>") };
                case "/static/client.js": return new(HttpStatusCode.OK) { Content = new StringContent("const config = {API_CLIENT_ID: 'test-client'};") };
                case "/v4/auth/login/":
                    loginRequests++;
                    var credentials = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));
                    Check(credentials.RootElement.GetProperty("username").GetString() == "test-user" && credentials.RootElement.GetProperty("password").GetString() == "test-password", "Username/password submitted in JSON body");
                    Check(!r.RequestUri.Query.Contains("password"), "Credentials absent from URL");
                    var login = Json(new { username = "test-user" }); login.Headers.Add("Set-Cookie", "sessionid=test; Secure; HttpOnly; Path=/"); return login;
                case "/v4/auth/o/authorize/":
                    var query = r.RequestUri.Query.TrimStart('?').Split('&').Select(x => x.Split('=',2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1])); state = query["state"];
                    Check(query["client_id"] == "test-client" && query["response_type"] == "code", "Session authorization request");
                    var authorize = new HttpResponseMessage(HttpStatusCode.Redirect);
                    authorize.Headers.Location = new("https://api.beatport.com/v4/auth/o/post-message/?code=test-code&state=" + state); return authorize;
                case "/v4/auth/o/token/":
                    var form = await r.Content!.ReadAsStringAsync(ct);
                    if (form.Contains("grant_type=refresh_token")) { refreshes++; Check(form.Contains("refresh_token=test-refresh"), "Refresh token submitted in body"); }
                    else Check(form.Contains("grant_type=authorization_code") && form.Contains("code=test-code"), "Authorization code exchanged");
                    return Json(new { access_token = refreshes == 0 ? "test-access" : "renewed-access", refresh_token = "test-refresh", expires_in = 3600 });
                default: throw new Exception("Unexpected endpoint");
            }
        });
        using (var session = await BeatportSession.LoginAsync(" test-user ", "test-password", default, transport))
        {
            Check(await session.AccessTokenAsync(default) == "test-access", "Login obtains access token without manual token input");
            Check(await session.AccessTokenAsync(default, true) == "renewed-access", "Session refreshes without retaining password");
            Check(loginRequests == 1 && docsRequests == 1 && refreshes == 1, "No repeated password login on refresh");
            int calls = 0;
            using var client = new BeatportClient(session, handler: new Handler((r, ct) => {
                calls++; if (calls == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                Check(r.Headers.Authorization?.Parameter == "renewed-access", "Refreshed token attached to API request");
                return Task.FromResult(Json(new { username = "test-user" }));
            }));
            await client.CheckAsync(default); Check(calls == 2, "401 read retried once after refresh");
        }
        using var failure = new Handler((r, ct) => Task.FromResult(r.RequestUri!.AbsolutePath == "/v4/docs/"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("API_CLIENT_ID: 'test-client'") }
            : Json(new { error = "test-password secret echoed by server" }, HttpStatusCode.Unauthorized)));
        try { using var s = await BeatportSession.LoginAsync("test-user", "test-password", default, failure); throw new Exception("Expected rejected login"); }
        catch (InvalidOperationException e) { Check(e.Message.Contains("401") && !e.Message.Contains("test-password"), "Login errors do not expose credentials or server bodies"); }
        foreach (var badRedirect in new[] { "https://evil.invalid/?code=secret", "https://api.beatport.com/v4/auth/o/post-message/?code=secret&state=wrong" })
        {
            using var handler = new Handler((r,ct) => {
                if (r.RequestUri!.AbsolutePath == "/v4/docs/") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("API_CLIENT_ID: 'test-client'") });
                if (r.RequestUri.AbsolutePath == "/v4/auth/login/") return Task.FromResult(Json(new { username = "test-user" }));
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect); redirect.Headers.Location = new(badRedirect); return Task.FromResult(redirect);
            });
            try { using var s = await BeatportSession.LoginAsync("test-user", "test-password", default, handler); throw new Exception("Expected redirect rejection"); }
            catch (InvalidOperationException e) { Check(!e.Message.Contains("secret"), "Unexpected redirect or state rejected without leaking code"); }
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { using var s = await BeatportSession.LoginAsync("test-user", "test-password", cancelled.Token, new Handler((r,ct) => { ct.ThrowIfCancellationRequested(); return Task.FromResult(Json(new {})); })); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { Check(true, "Login cancellation propagated"); }
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => reply(request,cancellationToken); }
}
