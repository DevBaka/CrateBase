using System.Net;
using System.Text;
using System.Text.Json;
using Cratebase.Beatport;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
static string Encode<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
var a = new Track { Id = 1, Name = "A Song", Mix = "Extended Mix", Artists = [new(10, "Artist")], LengthMs = 300000 };
Check(Deduplication.Same(a, a with { Id = 2, LengthMs = 301000 }), "Cross-release duplicate");
Check(!Deduplication.Same(a, a with { Id = 2, Mix = "Remix" }), "Remix preserved");
Check(!Deduplication.Same(a, a with { Id = 2, Artists = [new(20, "Other artist")] }), "Different artists preserved");
Check(!Deduplication.Same(a, a with { Id = 2, LengthMs = null }), "Missing duration stays conservative");
Check(!Deduplication.Same(a with { Isrc = "X" }, a with { Id = 2, Isrc = "X", Mix = "Radio Edit" }), "Shared ISRC does not collapse mixes");
Check(Deduplication.Plan([a, a with { Id = 2 }, a with { Id = 3, Mix = "Remix" }]).Keep.Count == 2, "Collection deduplication");
Check(Deduplication.Plan([a with { Id = 2 }], [a]).Keep.Count == 0, "Existing playlist duplicate skipped");
var temp = Path.Combine(Path.GetTempPath(), "beatport-tests-" + Guid.NewGuid(), "heard.json");
try { var h = new ListeningHistory(temp); await h.MarkAsync(a); var reload = new ListeningHistory(temp); await reload.LoadAsync(); Check(reload.Contains(a with { Id = 2 }), "Listening status survives restart and rerelease"); }
finally { if (File.Exists(temp)) File.Delete(temp); Directory.Delete(Path.GetDirectoryName(temp)!); }
var requests = new List<string>();
using (var api = new BeatportClient("test", handler: new Fake(async r => {
    requests.Add(r.RequestUri!.PathAndQuery);
    var second = r.RequestUri.Query.Contains("page=2&");
    var rows = second ? new[] { a with { Id = 2 } } : new[] { a };
    return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Encode(new { count = 2, next = second ? null : "next", results = rows }), Encoding.UTF8, "application/json") });
}))) { var rows = await api.TracksAsync(null, null, 10, default); Check(rows.Count == 2 && requests.Count == 2, "All pages collected"); }
using (var api = new BeatportClient("test", handler: new Fake(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)))))
{ try { await api.CheckAsync(default); throw new Exception("Expected auth failure"); } catch (InvalidOperationException e) { Check(e.Message.Contains("401"), "Expired token reported"); } }
using (var api = new BeatportClient("test", handler: new Fake(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Encode(new { count = 20, next = "next", results = new[] { a } })) }))))
{ try { await api.TracksAsync(null, null, 10, default); throw new Exception("Expected pagination failure"); } catch (InvalidOperationException e) { Check(e.Message.Contains("wiederholt"), "Repeated page rejected"); } }
using (var api = new BeatportClient("test", handler: new Fake(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Encode(new { count = 1, next = (string?)null, results = new[] { a } })) }))))
{ try { await api.TracksAsync(8, 99, null, default); throw new Exception("Expected subgenre verification failure"); } catch (InvalidOperationException e) { Check(e.Message.Contains("Subgenre"), "Unverified subgenre rejected"); } }
await AuthChecks.Run();
// Use a dedicated UI thread after async core tests.
Exception? uiError = null;
var thread = new Thread(() => {
    try {
        AppBuilder.Configure<Cratebase.Beatport.App.CratesApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new Cratebase.Beatport.App.MainWindow(); window.Show();
        var demo = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Demo laden");
        demo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        Check(list.Items.Count == 3, "Avalonia demo workflow renders deduplicated tracks");
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(Path.GetTempPath(), "cratebase-beatport-preview.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    } catch (Exception e) { uiError = e; }
});
if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
thread.Start(); thread.Join(); if (uiError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(uiError).Throw();
Console.WriteLine("All Beatport prototype checks passed.");
sealed class Fake(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => reply(request); }



