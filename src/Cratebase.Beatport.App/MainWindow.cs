using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Cratebase.Beatport.App;

public sealed class MainWindow : Window
{
    private readonly TextBox username = new() { PlaceholderText = "Beatport-Benutzername oder E-Mail" };
    private readonly TextBox password = new() { PlaceholderText = "Passwort · wird nicht gespeichert", PasswordChar = '●' };
    private readonly TextBox genreId = new() { Text = "8", PlaceholderText = "Genre-ID" }, subgenreId = new() { PlaceholderText = "Subgenre-ID" };
    private readonly TextBox artistQuery = new() { PlaceholderText = "Artist suchen …" }, playlistName = new() { PlaceholderText = "Name für neue Playlist" };
    private readonly ComboBox mode = new() { ItemsSource = new[] { "Subgenre", "Artist" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox genres = Combo(), subgenres = Combo(), artists = Combo(), playlists = Combo();
    private readonly CheckBox extended = new() { Content = "Nur Extended-Mixe", IsChecked = true }, streamable = new() { Content = "Nur Streaming-verfügbare Tracks", IsChecked = true };
    private readonly CheckBox confirmRemoval = new() { Content = "Geprüfte doppelte Playlist-Einträge entfernen" };
    private readonly TextBlock status = Text("Offline · Demo ausprobieren oder Beatport verbinden.", 14), count = Text("0 Tracks", 16);
    private readonly ListBox list = new();
    private readonly List<Button> actions = [];
    private readonly ListeningHistory history = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cratebase.Beatport", "heard.json"));
    private IBeatportCatalog? api;
    private readonly HashSet<long> membership = [];
    private List<Track> tracks = [];
    private List<(long Playlist, PlaylistItem Item)> removal = [];
    private CancellationTokenSource? cancellation;
    private bool busy, demo;
    private LibVLC? vlc; private MediaPlayer? player; private Media? media; private Track? playing;
    private readonly SemaphoreSlim historyLock = new(1);

    public MainWindow()
    {
        Title = "Cratebase · Beatport Crates / Prototyp"; Width = 1320; Height = 900; MinWidth = 960; MinHeight = 700;
        Background = Brush.Parse("#101513");
        var root = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(26), RowSpacing = 18 };
        var header = new StackPanel { Spacing = 5 };
        header.Children.Add(Text("CRATEBASE / BEATPORT CRATES", 13, "#B4F575")); header.Children.Add(Text("Ein Subgenre. Eine saubere Crate.", 32));
        header.Children.Add(Text("Eigenständiger Avalonia-Prototyp · wiederverwendbarer Beatport-Kern", 13, "#A7B7AE")); root.Children.Add(header);
        var connection = new Grid { ColumnDefinitions = new("*,*,Auto,Auto,Auto"), ColumnSpacing = 10 };
        connection.Children.Add(username); Put(connection, password, 1); Put(connection, Button("Anmelden", Connect), 2); Put(connection, Button("Demo laden", LoadDemo), 3);
        Put(connection, Button("Abmelden", () => { (api as IDisposable)?.Dispose(); api = null; password.Text = ""; membership.Clear(); playlists.ItemsSource = null; Refresh(); Say("Abgemeldet. Lokale Sitzung verworfen."); return Task.CompletedTask; }), 4);
        Grid.SetRow(connection, 1); root.Children.Add(connection);
        var columns = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 18 };
        var source = new StackPanel { Spacing = 9 }; source.Children.Add(Text("01 / QUELLE", 16, "#B4F575")); source.Children.Add(mode);
        source.Children.Add(new TextBlock { Text = "Genre und Subgenre laden oder IDs aus der Beatport-Filter-URL eingeben.", TextWrapping = TextWrapping.Wrap });
        var genreRow = new Grid { ColumnDefinitions = new("*,100,Auto"), ColumnSpacing = 7 }; genreRow.Children.Add(genres); Put(genreRow, genreId, 1); Put(genreRow, Button("Genres", async () => { RequireApi(); genres.ItemsSource = await api!.GenresAsync(Ct); }), 2); source.Children.Add(genreRow);
        var subRow = new Grid { ColumnDefinitions = new("*,100,Auto"), ColumnSpacing = 7 }; subRow.Children.Add(subgenres); Put(subRow, subgenreId, 1); Put(subRow, Button("Subgenres", async () => { RequireApi(); subgenres.ItemsSource = await api!.SubgenresAsync(Id(genreId.Text), Ct); }), 2); source.Children.Add(subRow);
        genres.SelectionChanged += (_, _) => { if (genres.SelectedItem is NamedEntity g) { genreId.Text = g.Id.ToString(); subgenres.ItemsSource = null; subgenreId.Text = ""; } };
        subgenres.SelectionChanged += (_, _) => { if (subgenres.SelectedItem is NamedEntity g) subgenreId.Text = g.Id.ToString(); };
        var search = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 7, IsVisible = false }; search.Children.Add(artistQuery); Put(search, Button("Suchen", async () => { RequireApi(); if (string.IsNullOrWhiteSpace(artistQuery.Text)) throw new InvalidOperationException("Artist-Namen eingeben."); artists.ItemsSource = await api!.ArtistsAsync(artistQuery.Text, Ct); Say("Artist auswählen. Maximal 100 Treffer; bei Bedarf Namen genauer eingeben."); }), 1); source.Children.Add(search); source.Children.Add(artists); artists.IsVisible = false;
        mode.SelectionChanged += (_, _) => { var byGenre = mode.SelectedIndex == 0; genreRow.IsVisible = byGenre; subRow.IsVisible = byGenre; search.IsVisible = !byGenre; artists.IsVisible = !byGenre; };
        source.Children.Add(extended); source.Children.Add(streamable); source.Children.Add(Button("Alle Seiten sammeln → Vorschau", Collect));
        columns.Children.Add(Panel(source));
        var target = new StackPanel { Spacing = 10 }; target.Children.Add(Text("02 / PLAYLIST", 16, "#B4F575")); target.Children.Add(playlists); target.Children.Add(playlistName);
        target.Children.Add(Text("Kein Ziel gewählt: neue Playlist mit dem eingegebenen Namen erstellen.", 12, "#A7B7AE"));
        target.Children.Add(Button("Vorschau mit Playlist abgleichen", Sync)); target.Children.Add(Button("Ziel auf Duplikate prüfen", Audit)); target.Children.Add(confirmRemoval); target.Children.Add(Button("Geprüfte Duplikate entfernen", Remove));
        target.Children.Add(Text("● Hellgrün: angehört     ● Dunkelgrün: in einer Playlist", 14, "#B5F7B8"));
        target.Children.Add(Text("Remixe bleiben getrennt. Keine automatische Bewertung der Musikqualität.", 12, "#A7B7AE"));
        Put(columns, Panel(target), 1); Grid.SetRow(columns, 2); root.Children.Add(columns);
        list.ItemTemplate = new FuncDataTemplate<Track>((t, _) => Row(t!)); Grid.SetRow(list, 3); root.Children.Add(list);
        var footer = new StackPanel { Spacing = 8 }; footer.Children.Add(count); footer.Children.Add(status);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var cancel = new Button { Content = "Vorgang abbrechen" }; cancel.Click += (_, _) => cancellation?.Cancel(); controls.Children.Add(cancel);
        var stop = new Button { Content = "■ Vorhören stoppen" }; stop.Click += (_, _) => player?.Stop(); controls.Children.Add(stop); footer.Children.Add(controls);
        Grid.SetRow(footer, 4); root.Children.Add(footer); Content = root;
        Opened += async (_, _) => { try { await history.LoadAsync(); } catch (Exception e) { Say($"Hörstatus konnte nicht geladen werden: {e.Message}"); } };
        Closed += (_, _) => { cancellation?.Cancel(); player?.Dispose(); media?.Dispose(); vlc?.Dispose(); (api as IDisposable)?.Dispose(); };
    }
    private CancellationToken Ct => cancellation?.Token ?? CancellationToken.None;
    private static long Id(string? text) => long.TryParse(text, out var n) && n > 0 ? n : throw new InvalidOperationException("Gültige Genre-/Subgenre-ID auswählen oder eingeben.");
    private static TextBlock Text(string s, double size, string? color = null) => new() { Text = s, FontSize = size, Foreground = Brush.Parse(color ?? "#EDF3EF"), TextWrapping = TextWrapping.Wrap };
    private static ComboBox Combo() => new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Auswählen …", ItemTemplate = new FuncDataTemplate<NamedEntity>((x, _) => Text(x is null ? "" : $"{x.Name} (#{x.Id})", 13)) };
    private static void Put(Grid grid, Control control, int column) { Grid.SetColumn(control, column); grid.Children.Add(control); }
    private static Border Panel(Control child) => new() { Child = child, Background = Brush.Parse("#1A231E"), CornerRadius = new(10), Padding = new(18) };
    private Button Button(string label, Func<Task> action)
    {
        var b = new Button { Content = label }; actions.Add(b); b.Click += async (_, _) => await Run(action); return b;
    }
    private void Say(string s) => status.Text = s;
    private async Task Run(Func<Task> action)
    {
        if (busy) return; busy = true; cancellation = new(); foreach (var b in actions) b.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) { Say("Abgebrochen. Bereits bestätigte Änderungen bleiben erhalten; erneuter Abgleich liest das Ziel neu ein."); }
        catch (Exception e) { Say(e.Message); }
        finally { busy = false; cancellation.Dispose(); cancellation = null; foreach (var b in actions) b.IsEnabled = true; }
    }
    private void RequireApi() { if (api is null || demo) throw new InvalidOperationException("Zuerst mit Beatport-Benutzername und Passwort anmelden. Demo schreibt keine Playlists."); }
    private void Refresh() { list.ItemsSource = null; list.ItemsSource = tracks; count.Text = $"{tracks.Count:N0} Tracks in der Vorschau"; }
    private Control Row(Track t)
    {
        var saved = membership.Contains(t.Id); var heard = history.Contains(t);
        var row = new Grid { ColumnDefinitions = new("*,70,Auto,Auto"), ColumnSpacing = 8, Margin = new(6) };
        var foreground = heard && !saved ? "#102817" : "#EDF3EF";
        var info = new StackPanel(); info.Children.Add(Text(t.Name + " · " + t.Mix, 15, foreground)); info.Children.Add(Text(t.ArtistText, 12, foreground)); row.Children.Add(info);
        Put(row, Text(t.Bpm?.ToString() ?? "—", 14, foreground), 1);
        var play = new Button { Content = "▶" }; play.Click += async (_, _) => await Run(() => Preview(t)); Put(row, play, 2);
        var mark = new Button { Content = "✓ Angehört" }; mark.Click += async (_, _) => await Run(() => Mark(t)); Put(row, mark, 3);
        return new Border { Child = row, Background = Brush.Parse(saved ? "#195B34" : heard ? "#B5F7B8" : "#202A24"), CornerRadius = new(5), Padding = new(5), Margin = new(0, 2) };
    }
    private async Task Mark(Track t)
    {
        await historyLock.WaitAsync(); try { await history.MarkAsync(t); } finally { historyLock.Release(); } Refresh();
    }
    private Task Preview(Track t)
    {
        if (string.IsNullOrWhiteSpace(t.SampleUrl)) { Say("Für diesen Track ist keine Hörprobe verfügbar. Manuell als angehört markieren ist möglich."); return Task.CompletedTask; }
        if (!Uri.TryCreate(t.SampleUrl, UriKind.Absolute, out var url) || url.Scheme != "https") throw new InvalidOperationException("Ungültige Hörproben-URL.");
        if (vlc is null)
        {
            LibVLCSharp.Shared.Core.Initialize(); vlc = new LibVLC("--no-video"); player = new MediaPlayer(vlc);
            player.Playing += (_, _) => { var current = playing; if (current is not null) Dispatcher.UIThread.Post(async () => { try { await Mark(current); } catch (Exception e) { Say(e.Message); } }); };
            player.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() => Say("Hörprobe konnte nicht abgespielt werden."));
        }
        player!.Stop(); media?.Dispose(); media = new Media(vlc, url); playing = t; player.Play(media); Say($"Vorhören: {t.Display}"); return Task.CompletedTask;
    }
    private async Task Connect()
    {
        Say("Bei Beatport anmelden …");
        BeatportSession session;
        try { session = await BeatportSession.LoginAsync(username.Text ?? "", password.Text ?? "", Ct); }
        finally { password.Text = ""; }
        var client = new BeatportClient(session, new Progress<string>(Say));
        try { await client.CheckAsync(Ct); } catch { client.Dispose(); throw; }
        (api as IDisposable)?.Dispose(); api = client; demo = false; tracks = []; removal = []; membership.Clear();
        await RefreshPlaylists(); Refresh(); Say("Verbunden. Playlist-Mitgliedschaften eingelesen. Genres laden oder Artist suchen.");
    }
    private async Task RefreshPlaylists()
    {
        var all = await api!.PlaylistsAsync(Ct); var next = new HashSet<long>();
        foreach (var p in all) foreach (var entry in await api.PlaylistTracksAsync(p.Id, Ct)) next.Add(entry.Track.Id);
        membership.Clear(); membership.UnionWith(next); playlists.ItemsSource = all;
    }
    private async Task Collect()
    {
        RequireApi(); tracks = []; removal = []; Refresh();
        var rows = await api!.TracksAsync(mode.SelectedIndex == 0 ? Id(genreId.Text) : null, mode.SelectedIndex == 0 ? Id(subgenreId.Text) : null,
            mode.SelectedIndex == 1 ? (artists.SelectedItem as NamedEntity)?.Id ?? throw new InvalidOperationException("Artist auswählen.") : null, Ct);
        var filtered = rows.Where(t => (extended.IsChecked != true || Regex.IsMatch(t.Mix, @"\bextended\b", RegexOptions.IgnoreCase)) && (streamable.IsChecked != true || t.Streamable == true)).ToArray();
        var plan = Deduplication.Plan(filtered); tracks = plan.Keep.ToList(); Refresh(); Say($"{rows.Count:N0} geladen · {rows.Count - filtered.Length:N0} ausgefiltert · {plan.Duplicates.Count:N0} Duplikate übersprungen.");
    }
    private async Task Sync()
    {
        RequireApi(); if (tracks.Count == 0) throw new InvalidOperationException("Zuerst Tracks sammeln.");
        var target = playlists.SelectedItem as NamedEntity;
        if (target is null)
        {
            if (string.IsNullOrWhiteSpace(playlistName.Text)) throw new InvalidOperationException("Ziel auswählen oder neuen Playlist-Namen eingeben.");
            target = await api!.CreatePlaylistAsync(playlistName.Text.Trim(), Ct);
            var all = ((IEnumerable<NamedEntity>?)playlists.ItemsSource ?? []).Append(target).ToArray(); playlists.ItemsSource = all; playlists.SelectedItem = target;
        }
        var existing = await api!.PlaylistTracksAsync(target.Id, Ct); var todo = Deduplication.Plan(tracks, existing.Select(i => i.Track)).Keep; int done = 0;
        foreach (var batch in todo.Chunk(50)) { await api.AddAsync(target.Id, batch.Select(t => t.Id), Ct); foreach (var t in batch) membership.Add(t.Id); done += batch.Length; Say($"{done:N0}/{todo.Count:N0} Tracks bestätigt."); Refresh(); }
        Say($"{done:N0} Tracks hinzugefügt · {tracks.Count - todo.Count:N0} bereits vorhanden. Playlist in Traktor neu laden.");
    }
    private async Task Audit()
    {
        RequireApi(); var p = playlists.SelectedItem as NamedEntity ?? throw new InvalidOperationException("Bestehende Zielplaylist auswählen.");
        var rows = await api!.PlaylistTracksAsync(p.Id, Ct); var keep = new List<Track>(); removal = [];
        foreach (var row in rows) { if (Deduplication.Plan([row.Track], keep).Duplicates.Count > 0) removal.Add((p.Id, row)); else keep.Add(row.Track); }
        tracks = removal.Select(r => r.Item.Track).ToList(); confirmRemoval.IsChecked = false; Refresh(); Say($"{removal.Count:N0} doppelte Einträge gefunden. Vorschau prüfen und Entfernung ausdrücklich aktivieren.");
    }
    private async Task Remove()
    {
        RequireApi(); if (removal.Count == 0 || confirmRemoval.IsChecked != true) throw new InvalidOperationException("Duplikate prüfen und Entfernung aktivieren.");
        while (removal.Count > 0) { var entry = removal[0]; await api!.RemoveAsync(entry.Playlist, entry.Item.Id, Ct); removal.RemoveAt(0); tracks = removal.Select(r => r.Item.Track).ToList(); Refresh(); }
        confirmRemoval.IsChecked = false; await RefreshPlaylists(); Say("Geprüfte doppelte Einträge entfernt. Erste Versionen bleiben in der Playlist.");
    }
    private Task LoadDemo()
    {
        (api as IDisposable)?.Dispose(); api = null; password.Text = ""; demo = true; removal = []; membership.Clear(); playlists.ItemsSource = null;
        var a = new NamedEntity(1, "Demo Artist");
        var original = new Track { Id = 900000001, Name = "Night Signals [DEMO]", Mix = "Extended Mix", Artists = [a], LengthMs = 360000, Bpm = 150 };
        var source = new[] { original, original with { Id = 900000002 }, original with { Id = 900000003, Mix = "Club Remix" }, original with { Id = 900000004, Name = "After Hours [DEMO]" } };
        var plan = Deduplication.Plan(source); tracks = plan.Keep.ToList(); membership.Add(900000004); Refresh(); Say("DEMO · 4 fiktive Tracks → 3 nach Duplikatprüfung. Kein Beatport-Zugriff, keine Hörproben. „Angehört“ testet die lokale Markierung."); return Task.CompletedTask;
    }
}


