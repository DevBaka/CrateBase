using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cratebase.App.Desktop.Services;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.App.Desktop.Views;
using Cratebase.Data;
using Cratebase.Data.Repositories;
using Cratebase.Core.Models;
using Track=Cratebase.Core.Models.Track;

internal static class WorkspaceShortcutUiChecks
{
    public static void Run(WorkspaceWindow window,WorkspacePlayer player,TrackRepository tracks,LibraryRootRepository roots,PlaylistRepository playlists,WorkspaceStateStore state,string directory)
    {
        var keyboardButton=window.GetLogicalDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Tasten"));
        Until(()=>keyboardButton.IsEnabled);
        var buttons=window.GetLogicalDescendants().OfType<ToggleButton>().Where(b=>b.Content is string s&&new[]{"Club","Extended","Intro","Trash"}.Contains(s)).ToArray();
        Check(buttons.Select(b=>b.Content).SequenceEqual(new[]{"Club","Extended","Intro","Trash"}),"Player tag buttons are ordered Club, Extended, Intro, Trash");
        Check(buttons.All(b=>!b.IsEnabled),"Player tag buttons stay disabled without a loaded track");
        var root=Pump(roots.AddOrGetAsync(directory,"Test audio"));
        var track=new Track {LibraryRootId=root.Id,RelativePath="player.wav",Title="Player track",Format="wav"};
        track.Id=Pump(tracks.UpsertAsync(track));File.WriteAllBytes(Path.Combine(directory,track.RelativePath),[0]);
        Pump(player.PlayAsync(track,[track]));
        Check(buttons.All(b=>b.IsEnabled&&b.IsChecked!=true),"Loading a track enables its actual player tag states");
        var panes=(TrackPaneViewModel[])Field("_panes").GetValue(window)!;
        Pump(panes[0].OpenAsync(new(LibraryTargetKind.Playlist,"Other playlist",-999)));
        foreach(var button in buttons)Click(button);
        Until(()=>track.ClubReady&&track.Extended&&track.Intro&&track.Trash);
        var stored=Pump(tracks.GetByIdAsync(track.Id))!;
        Check(stored.ClubReady&&stored.Extended&&stored.Intro&&stored.Trash&&buttons.All(b=>b.IsChecked==true),"Player buttons persist all marks while a different browser source is open");
        var settings=(KeyboardSettings)Field("_keyboard").GetValue(window)!;
        var chord=new ShortcutBinding {Key=Key.F9,Modifiers=KeyModifiers.Control,Action=ShortcutAction.ClubToggle};settings.ActiveProfile.Bindings.Add(chord);
        window.Activate();buttons[0].Focus();
        Down(window,chord);Until(()=>!track.ClubReady);Down(window,chord);Dispatcher.UIThread.RunJobs();
        Check(!track.ClubReady,"Holding a focused shortcut triggers only once");
        Up(window,chord);Down(window,chord);Until(()=>track.ClubReady);Up(window,chord);
        Check(track.ClubReady,"Releasing a shortcut allows the next physical press");
        var text=window.GetVisualDescendants().OfType<TextBox>().First(t=>t.IsVisible&&t.IsEnabled);
        text.Focus();Down(window,chord);Dispatcher.UIThread.RunJobs();Up(window,chord);
        Check(track.ClubReady,"Focused shortcuts do not toggle tags while typing in a text box");
        var playlist=Pump(playlists.CreateAsync("Keyboard target",PlaylistNodeType.Playlist,null));
        var favorites=(FavoriteProfileSettings)Field("_favorites").GetValue(window)!;
        favorites.Profiles[0].Targets[0]=new(LibraryTargetKind.Playlist,"Keyboard target",playlist.Id);
        var slotChord=new ShortcutBinding {Key=Key.F11,Modifiers=KeyModifiers.Control,Action=ShortcutAction.FavoriteSlot,Slot=0};settings.ActiveProfile.Bindings.Add(slotChord);
        buttons[0].Focus();Down(window,slotChord);Up(window,slotChord);
        Until(()=>Pump(tracks.SearchAsync(null,[playlist.Id],true,100,0)).Any(t=>t.Id==track.Id));
        Check(Pump(tracks.SearchAsync(null,[playlist.Id],true,100,0)).Count==1,"Keyboard favorite action adds the player track through the shared target workflow");
        Click(keyboardButton);
        Window? dialog=null;Until(()=> {dialog=window.OwnedWindows.FirstOrDefault(w=>w.IsVisible&&w.Title!.StartsWith("Tastatur"));return dialog!=null;});
        var content=dialog!;
        Click(content.GetLogicalDescendants().OfType<Button>().First(b=>Equals(b.Content,"+ Belegung")));
        Click(content.GetLogicalDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Taste festlegen …")));
        Down(content,new ShortcutBinding {Key=Key.F10,Modifiers=KeyModifiers.Control});
        Check(content.GetLogicalDescendants().OfType<Button>().Any(b=>Equals(b.Content,new KeyGesture(Key.F10,KeyModifiers.Control).ToString())),"Keyboard dialog learns a pressed chord");
        Click(content.GetLogicalDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Speichern")));
        Until(()=>!content.IsVisible);
        var saved=Pump(state.ReadAsync("keyboard-shortcuts-v1"))!;
        Check(System.Text.Json.JsonSerializer.Deserialize<KeyboardSettings>(saved)!.ActiveProfile.Bindings.Any(b=>b.Key==Key.F10),"Learned shortcuts are saved persistently");
        var mode=window.GetLogicalDescendants().OfType<ComboBox>().Single(c=>c.ItemsSource is string[] modes&&modes.Contains("Medientasten: Tags"));
        mode.SelectedIndex=1;
        Until(()=>System.Text.Json.JsonSerializer.Deserialize<KeyboardSettings>(Pump(state.ReadAsync("keyboard-shortcuts-v1"))!)!.ActiveProfile.MediaKeysTagMode);
        Check(((KeyboardSettings)Field("_keyboard").GetValue(window)!).ActiveProfile.MediaKeysTagMode,"Media-key mode selector persists the active profile mode");
        var missing=new Track {LibraryRootId=root.Id,RelativePath="not-here.wav",Title="Missing UI track"};missing.Id=Pump(tracks.UpsertAsync(missing));
        Pump(player.PlayAsync(missing,[missing,track]));
        Pump(panes[0].OpenAsync(new(LibraryTargetKind.UnreachableFiles,"Datei nicht erreichbar")));
        Check(panes[0].Tracks.Single().Id==missing.Id&&panes[0].Tracks[0].FileAvailability=="Datei nicht erreichbar","Missing-file browser source lists the skipped track with its persistent status tag");
        File.WriteAllBytes(Path.Combine(directory,missing.RelativePath),[0]);Pump(player.PlayAsync(missing,[missing,track]));
        Until(()=>panes[0].Total==0);
        Check(panes[0].Tracks.Count==0,"The open missing-file view refreshes automatically when playback verifies a restored file");
        Click(window.GetLogicalDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Club exportieren")));
        Window? export=null;Until(()=> {export=window.OwnedWindows.FirstOrDefault(w=>w.IsVisible&&w.Title=="Club Collection · Dateien und NML exportieren");return export!=null;});
        var target=Path.Combine(directory,"UiExport");
        var destination=export!.GetLogicalDescendants().OfType<TextBox>().First(t=>t.Text?.EndsWith("ClubCollection")==true);
        Check(destination.Text==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Music","ClubCollection"),"Full export defaults to the requested Music/ClubCollection folder");
        destination.Text=target;
        TextBox? genreFolder=null;Until(()=> {genreFolder=export!.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t=>t.PlaceholderText=="Genre/Untergenre");return genreFolder!=null;});
        genreFolder!.Text="Techno/Hard Techno";
        Click(export!.GetLogicalDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Export starten")));
        Window? complete=null;Until(()=> {complete=window.OwnedWindows.FirstOrDefault(w=>w.IsVisible&&w.Title=="Club-Export abgeschlossen");return complete!=null;});
        Click(complete!.GetLogicalDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Schließen")));
        Until(()=>Field("_job").GetValue(window)==null);
        var layout=System.Text.Json.JsonSerializer.Deserialize<WorkspaceSettings>(Pump(state.ReadAsync("workspace-layout"))!)!;
        Check(layout.ClubExportDirectory==target&&layout.ClubExportFolders.Values.Contains("Techno/Hard Techno"),"Export dialog saves the changed destination and edited genre mapping");
        Check(File.Exists(Path.Combine(target,Cratebase.Import.ClubMediaExporter.NmlName))&&Directory.GetFiles(Path.Combine(target,"Techno","Hard Techno")).Length==1,"Export dialog completes an audio-and-NML package in the chosen folder");
    }
    private static FieldInfo Field(string name)=>typeof(WorkspaceWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!;
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Down(Control control,ShortcutBinding binding)=>control.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=binding.Key,KeyModifiers=binding.Modifiers});
    private static void Up(Control control,ShortcutBinding binding)=>control.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyUpEvent,Key=binding.Key,KeyModifiers=binding.Modifiers});
    private static T Pump<T>(Task<T> task){Until(()=>task.IsCompleted);return task.GetAwaiter().GetResult();}
    private static void Pump(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void Until(Func<bool> condition)
    {var deadline=DateTime.UtcNow.AddSeconds(8);while(!condition()){if(DateTime.UtcNow>deadline)throw new TimeoutException("Workspace keyboard UI timed out");Dispatcher.UIThread.RunJobs();Thread.Sleep(10);}Dispatcher.UIThread.RunJobs();}
    private static void Check(bool condition,string text){if(!condition)throw new Exception(text);Console.WriteLine("PASS: "+text);}
}
