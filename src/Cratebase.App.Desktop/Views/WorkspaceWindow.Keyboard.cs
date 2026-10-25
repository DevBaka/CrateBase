using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cratebase.App.Desktop.Services;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Views;

public sealed partial class WorkspaceWindow
{
    private KeyboardSettings _keyboard=new();
    private WindowsGlobalHotkeys? _globalHotkeys;
    private IReadOnlyList<HotkeyRegistration> _hotkeyResults=[];
    private readonly HashSet<Key> _heldKeys=new();
    private readonly HashSet<Guid> _shortcutRunning=new();
    private bool _keyboardSuspended;
    private readonly Button _keyboardButton=new() {Content="Tasten",Padding=new Thickness(10,7),IsEnabled=false};
    private readonly CheckBox _globalKeysToggle=new() {Content="Global",Margin=new Thickness(6,0),IsEnabled=false};
    private readonly ComboBox _mediaKeysMode=new() {ItemsSource=new[]{"Medientasten: Wiedergabe","Medientasten: Tags"},SelectedIndex=0,IsEnabled=false,MinWidth=215};
    private bool _keyboardToggleUpdating;

    private void WireKeyboard()
    {
        AddHandler(KeyDownEvent,KeyboardDown,RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent,(_,e)=>_heldKeys.Remove(e.Key),RoutingStrategies.Tunnel,true);
        Deactivated+=(_,_)=>_heldKeys.Clear();
        _keyboardButton.Click+=async (_,_)=>await Safe(KeyboardDialog);
        _mediaKeysMode.SelectionChanged+=async (_,_)=>
        {
            if(_keyboardToggleUpdating)return;
            _keyboard.ActiveProfile.MediaKeysTagMode=_mediaKeysMode.SelectedIndex==1;
            await Safe(async ()=> {ApplyKeyboard();await SaveKeyboard();});
        };
        ToolTip.SetTip(_mediaKeysMode,"Wiedergabe: Zurück / Play-Pause / Weiter. Tags: Zurück = Club, Play-Pause = Extended, Weiter = Intro. Gilt für die Windows-Mediensitzung auch im Hintergrund, unabhängig vom Schalter für eigene globale Hotkeys.");
        _globalKeysToggle.IsCheckedChanged+=async (_,_)=>
        {
            if(_keyboardToggleUpdating)return;
            _keyboard.GlobalEnabled=_globalKeysToggle.IsChecked==true;
            await Safe(async ()=> {ApplyKeyboard();await SaveKeyboard();});
        };
        ToolTip.SetTip(_globalKeysToggle,"Globale Hotkeys ein/aus. Aktivierte Kombinationen sind auch in anderen Apps reserviert. Im Tasten-Dialog Profile und Belegungen verwalten.");
    }
    private async Task InitializeKeyboardAsync()
    {
        var saved=await _state.ReadAsync("keyboard-shortcuts-v1");
        try
        {
            _keyboard=JsonSerializer.Deserialize<KeyboardSettings>(saved??"{}")??new();
            KeyboardShortcuts.Validate(_keyboard);
        }
        catch(Exception ex) { _keyboard=new();Status("Tastaturkonfiguration konnte nicht geladen werden: "+ex.Message); }
        if(OperatingSystem.IsWindows()&&TryGetPlatformHandle() is {HandleDescriptor:"HWND"} handle)
        {
            _globalHotkeys=new(handle.Handle);
            _globalHotkeys.Pressed+=binding=>Dispatcher.UIThread.Post(async ()=>
            {
                if(_closing||_keyboardSuspended||!_keyboard.GlobalEnabled||OwnedWindows.Any(w=>w.IsVisible))return;
                if(IsActive&&KeyboardEditing())return;
                if(_keyboard.ActiveProfile.Bindings.Any(b=>b.Id==binding.Id&&b.Enabled&&b.Scope==ShortcutScope.Global))await RunShortcut(binding);
            });
            Win32Properties.AddWndProcHookCallback(this,KeyboardWndProc);
        }
        ApplyKeyboard();
        _keyboardButton.IsEnabled=true;
        _globalKeysToggle.IsEnabled=_globalHotkeys!=null;
        _mediaKeysMode.IsEnabled=OperatingSystem.IsWindows();
    }
    private IntPtr KeyboardWndProc(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(_globalHotkeys?.HandleMessage(message,wParam)==true)handled=true;
        return IntPtr.Zero;
    }
    private bool KeyboardEditing()
        => FocusManager?.GetFocusedElement() is Visual visual && visual.GetSelfAndVisualAncestors().Any(v=>v is TextBox or AutoCompleteBox or NumericUpDown or ComboBox);
    private async void KeyboardDown(object? sender,KeyEventArgs e)
    {
        if(_keyboardSuspended||_closing||OwnedWindows.Any(w=>w.IsVisible))return;
        var binding=KeyboardShortcuts.Match(_keyboard,e.Key,e.KeyModifiers,KeyboardEditing(),IsActive);
        if(binding==null)return;
        e.Handled=true;
        if(!_heldKeys.Add(e.Key))return;
        await RunShortcut(binding);
    }
    private async Task RunShortcut(ShortcutBinding binding)
    {
        if(!_shortcutRunning.Add(binding.Id))return;
        try
        {
            await Safe(async ()=>
            {
                switch(binding.Action)
                {
                    case ShortcutAction.PlayPause:_player.ToggleCommand.Execute(null);break;
                    case ShortcutAction.Next:await _player.NextCommand.ExecuteAsync(null);break;
                    case ShortcutAction.Previous:await _player.PreviousCommand.ExecuteAsync(null);break;
                    case ShortcutAction.FavoriteSlot:
                        if(_player.CurrentTrack is not {} track) {Status("Kein Track im Player geladen.");return;}
                        var target=ActiveProfile?.Targets.GetValueOrDefault(binding.Slot);
                        if(target==null) {Status($"Favoriten-Slot {binding.Slot+1} ist im aktuellen Favoritenprofil nicht belegt.");return;}
                        await AddToTarget(target,[track]);break;
                    case ShortcutAction.ClubToggle:await TogglePlayerFlag("club");break;
                    case ShortcutAction.ExtendedToggle:await TogglePlayerFlag("extended");break;
                    case ShortcutAction.IntroToggle:await TogglePlayerFlag("intro");break;
                    case ShortcutAction.RecorderMarker:
                        if(!_recorder.IsRecording) {Status("Für einen Übergangsmarker muss eine Set-Aufnahme laufen.");return;}
                        _recorder.MarkTransitionCommand.Execute(null);break;
                    case ShortcutAction.MediaKeysModeToggle:
                        _keyboard.ActiveProfile.MediaKeysTagMode=!_keyboard.ActiveProfile.MediaKeysTagMode;
                        ApplyKeyboard();await SaveKeyboard();Status(_keyboard.ActiveProfile.MediaKeysTagMode?"Medientasten: Tags · Zurück = Club, Play/Pause = Extended, Weiter = Intro.":"Medientasten: Wiedergabe.");break;
                }
            });
        }
        finally {_shortcutRunning.Remove(binding.Id);}
    }
    private Task SaveKeyboard()=>_state.WriteAsync("keyboard-shortcuts-v1",JsonSerializer.Serialize(_keyboard));
    private void ApplyKeyboard()
    {
        _heldKeys.Clear();
        var bindings=_keyboard.GlobalEnabled&&!_keyboardSuspended&&!_closing?_keyboard.ActiveProfile.Bindings:Array.Empty<ShortcutBinding>().AsEnumerable();
        _hotkeyResults=_globalHotkeys?.Apply(bindings)??bindings.Where(b=>b.Enabled&&b.Scope==ShortcutScope.Global).Select(b=>new HotkeyRegistration(b,false,"Globale Hotkeys benötigen ein Windows-Fenster.")).ToArray();
        _keyboardToggleUpdating=true;_globalKeysToggle.IsChecked=_keyboard.GlobalEnabled;_mediaKeysMode.SelectedIndex=_keyboard.ActiveProfile.MediaKeysTagMode?1:0;_keyboardToggleUpdating=false;
        _mediaSession?.SetTagMode(_keyboard.ActiveProfile.MediaKeysTagMode);
        var failures=_hotkeyResults.Count(r=>!r.Success);
        _keyboardButton.Content=failures>0?$"Tasten ⚠ {failures}":"Tasten";
        ToolTip.SetTip(_keyboardButton,$"Profil: {_keyboard.ActiveProfile.Name}\nFokus: {(_keyboard.FocusedEnabled?"ein":"aus")} · Global: {(_keyboard.GlobalEnabled?"ein":"aus")}\n"+string.Join("\n",_hotkeyResults.Select(r=>r.Binding.Gesture+": "+r.Message)));
        var menu=new ContextMenu();
        foreach(var profile in _keyboard.Profiles)
            menu.Items.Add(Menu((profile.Id==_keyboard.ActiveProfile.Id?"✓ ":"")+profile.Name,async ()=> {_keyboard.ActiveProfileId=profile.Id;ApplyKeyboard();await SaveKeyboard();Status("Tastaturprofil: "+profile.Name);}));
        _keyboardButton.ContextMenu=menu;
        if(failures>0)Status("Globale Hotkeys: "+string.Join(" · ",_hotkeyResults.Where(r=>!r.Success).Select(r=>r.Binding.Gesture+": "+r.Message)));
    }

    private sealed record ShortcutChoice(ShortcutAction Action)
    {public override string ToString()=>KeyboardShortcuts.ActionLabel(Action);}
    private async Task KeyboardDialog()
    {
        var draft=JsonSerializer.Deserialize<KeyboardSettings>(JsonSerializer.Serialize(_keyboard))!;
        var previousResults=_hotkeyResults;
        _keyboardSuspended=true;_globalHotkeys?.Dispose();_heldKeys.Clear();
        try
        {
            var dialog=Dialog("Tastatur · Profile und globale Hotkeys",1000,700);
            var layout=new Grid {RowDefinitions=new("Auto,Auto,Auto,*,Auto"),Margin=new Thickness(18)};
            var profileRow=Row();var picker=new ComboBox {Width=230};profileRow.Children.Add(Label("PROFIL"));profileRow.Children.Add(picker);
            var name=new TextBox {Width=200,PlaceholderText="Profilname"};profileRow.Children.Add(name);
            void RefreshProfiles(Guid? id)
            {picker.ItemsSource=null;picker.ItemsSource=draft.Profiles;picker.SelectedItem=draft.Profiles.FirstOrDefault(p=>p.Id==id)??draft.Profiles[0];}
            KeyboardProfile Selected()=>picker.SelectedItem as KeyboardProfile??draft.Profiles[0];
            name.TextChanged+=(_,_)=> {if(picker.SelectedItem is KeyboardProfile profile)profile.Name=name.Text??"";};
            profileRow.Children.Add(Button("+ Profil",()=> {var profile=new KeyboardProfile {Name="Profil "+(draft.Profiles.Count+1)};draft.Profiles.Add(profile);RefreshProfiles(profile.Id);return Task.CompletedTask;}));
            profileRow.Children.Add(Button("Kopieren",()=> {var copy=JsonSerializer.Deserialize<KeyboardProfile>(JsonSerializer.Serialize(Selected()))!;copy.Id=Guid.NewGuid();copy.Name+=" Kopie";copy.Bindings=copy.Bindings.Select(b=>b with {Id=Guid.NewGuid()}).ToList();draft.Profiles.Add(copy);RefreshProfiles(copy.Id);return Task.CompletedTask;}));
            profileRow.Children.Add(Button("Löschen",()=> {if(draft.Profiles.Count>1){draft.Profiles.Remove(Selected());RefreshProfiles(null);}return Task.CompletedTask;}));layout.Children.Add(profileRow);
            var switches=Row();Grid.SetRow(switches,1);
            var focused=new CheckBox {Content="Belegungen im App-Fokus aktiv",IsChecked=draft.FocusedEnabled};
            var global=new CheckBox {Content="Globale Hotkeys aktiv",IsChecked=draft.GlobalEnabled};switches.Children.Add(focused);switches.Children.Add(global);layout.Children.Add(switches);
            var mediaMode=new ComboBox {ItemsSource=new[]{"Medientasten: Wiedergabe","Medientasten: Tags"},MinWidth=215};switches.Children.Add(mediaMode);
            var explanation=new TextBlock {Text="Fokus-Belegungen gelten im Hauptfenster und pausieren in Textfeldern und Dialogen. Globale Hotkeys reservieren die Kombination auch in anderen Apps. Windows erkennt bereits registrierte Hotkeys; normale Photoshop-/WoW-/DaVinci-Shortcuts lassen sich nicht automatisch erkennen. Seltene Kombinationen wählen, z. B. Strg+Alt+Shift+F9. Global ist anfangs aus; Profile und Ein/Aus sind oben auch direkt erreichbar. Favoriten-Slots beziehen sich auf das aktuelle Favoritenprofil.",TextWrapping=TextWrapping.Wrap,Foreground=Muted,Margin=new Thickness(0,10)};Grid.SetRow(explanation,2);layout.Children.Add(explanation);
            var list=new StackPanel {Spacing=10};var scroll=new ScrollViewer {Content=list};Grid.SetRow(scroll,3);layout.Children.Add(scroll);
            var feedback=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Orange};
            ShortcutBinding? learning=null;Button? learningButton=null;
            void Render()
            {
                learning=null;learningButton=null;list.Children.Clear();
                foreach(var scope in new[]{ShortcutScope.Focused,ShortcutScope.Global})
                {
                    list.Children.Add(Label(scope==ShortcutScope.Focused?"IM APP-FOKUS":"GLOBAL · AUCH IM HINTERGRUND"));
                    foreach(var binding in Selected().Bindings.Where(b=>b.Scope==scope).ToArray())
                    {
                        var row=new Grid {ColumnDefinitions=new("Auto,*,130,220,Auto"),Margin=new Thickness(0,2)};
                        var enabled=new CheckBox {IsChecked=binding.Enabled,Margin=new Thickness(0,0,8,0)};enabled.IsCheckedChanged+=(_,_)=>binding.Enabled=enabled.IsChecked==true;row.Children.Add(enabled);
                        var action=new ComboBox {ItemsSource=Enum.GetValues<ShortcutAction>().Select(a=>new ShortcutChoice(a)).ToArray(),HorizontalAlignment=HorizontalAlignment.Stretch};action.SelectedItem=action.ItemsSource!.Cast<ShortcutChoice>().First(c=>c.Action==binding.Action);Grid.SetColumn(action,1);row.Children.Add(action);
                        var slot=new NumericUpDown {Minimum=1,Maximum=64,Value=binding.Slot+1,Width=115,IsVisible=binding.Action==ShortcutAction.FavoriteSlot};Grid.SetColumn(slot,2);row.Children.Add(slot);
                        slot.ValueChanged+=(_,_)=>binding.Slot=(int)(slot.Value??1)-1;
                        action.SelectionChanged+=(_,_)=> {if(action.SelectedItem is ShortcutChoice choice){binding.Action=choice.Action;slot.IsVisible=choice.Action==ShortcutAction.FavoriteSlot;}};
                        var learn=Button(binding.Key==Key.None?"Taste festlegen …":binding.Gesture,()=>
                        {
                            if(learningButton!=null)learningButton.Content=learning!.Key==Key.None?"Taste festlegen …":learning.Gesture;
                            learning=binding;learningButton=null;feedback.Text="Kombination drücken. Escape bricht das Lernen ab.";return Task.CompletedTask;
                        });
                        learn.Click+=(_,_)=> {if(learning==binding){learningButton=learn;learn.Content="Taste drücken …";}};
                        Grid.SetColumn(learn,3);row.Children.Add(learn);
                        var remove=Button("×",()=> {Selected().Bindings.Remove(binding);Render();return Task.CompletedTask;});Grid.SetColumn(remove,4);row.Children.Add(remove);list.Children.Add(row);
                        if(previousResults.FirstOrDefault(r=>r.Binding.Id==binding.Id) is {Success:false} result)list.Children.Add(new TextBlock {Text=result.Binding.Gesture+": "+result.Message,Foreground=Brushes.Orange,TextWrapping=TextWrapping.Wrap});
                    }
                    list.Children.Add(Button("+ Belegung",()=> {Selected().Bindings.Add(new() {Scope=scope});Render();return Task.CompletedTask;}));
                }
            }
            var changingProfile=false;
            mediaMode.SelectionChanged+=(_,_)=> {if(!changingProfile)Selected().MediaKeysTagMode=mediaMode.SelectedIndex==1;};
            picker.SelectionChanged+=(_,_)=> {name.Text=Selected().Name;changingProfile=true;mediaMode.SelectedIndex=Selected().MediaKeysTagMode?1:0;changingProfile=false;Render();};RefreshProfiles(draft.ActiveProfileId);
            dialog.AddHandler(KeyDownEvent,(_,e)=>
            {
                if(learning==null)return;e.Handled=true;
                if(KeyboardShortcuts.IsModifier(e.Key))return;
                if(e.Key==Key.Escape) {if(learningButton!=null)learningButton.Content=learning.Key==Key.None?"Taste festlegen …":learning.Gesture;learning=null;learningButton=null;feedback.Text="Lernen abgebrochen.";return;}
                var candidate=learning with {Key=e.Key,Modifiers=e.KeyModifiers};
                if(KeyboardShortcuts.Error(candidate) is {} error){feedback.Text=error;return;}
                learning.Key=e.Key;learning.Modifiers=e.KeyModifiers;
                if(learningButton!=null)learningButton.Content=learning.Gesture;
                learning=null;learningButton=null;feedback.Text="Kombination übernommen. Zum Aktivieren speichern.";
            },RoutingStrategies.Tunnel);
            var footer=new StackPanel {Spacing=8,Margin=new Thickness(0,10,0,0)};footer.Children.Add(feedback);var buttons=Row();
            buttons.Children.Add(Button("Speichern",async ()=>
            {
                try
                {
                    draft.FocusedEnabled=focused.IsChecked==true;draft.GlobalEnabled=global.IsChecked==true;draft.ActiveProfileId=Selected().Id;
                    KeyboardShortcuts.Validate(draft);
                    await _state.WriteAsync("keyboard-shortcuts-v1",JsonSerializer.Serialize(draft));_keyboard=draft;dialog.Close();
                }
                catch(Exception ex){feedback.Text=ex.Message;}
            }));
            buttons.Children.Add(Button("Abbrechen",()=> {dialog.Close();return Task.CompletedTask;}));footer.Children.Add(buttons);Grid.SetRow(footer,4);layout.Children.Add(footer);dialog.Content=layout;
            await dialog.ShowDialog(this);
        }
        finally {_keyboardSuspended=false;ApplyKeyboard();}
    }
    private void DisposeKeyboard()
    {
        _keyboardSuspended=true;_globalHotkeys?.Dispose();_heldKeys.Clear();
        if(OperatingSystem.IsWindows())Win32Properties.RemoveWndProcHookCallback(this,KeyboardWndProc);
    }
}
