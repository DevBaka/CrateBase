using Avalonia.Input;
using System.Text.Json.Serialization;

namespace Cratebase.App.Desktop.Services;

public enum ShortcutScope { Focused, Global }
public enum ShortcutAction { PlayPause, Next, Previous, FavoriteSlot, ClubToggle, ExtendedToggle, IntroToggle, RecorderMarker, MediaKeysModeToggle }
public sealed record ShortcutBinding
{
    public Guid Id { get; init; }=Guid.NewGuid();
    public Key Key { get; set; }
    public KeyModifiers Modifiers { get; set; }
    public ShortcutScope Scope { get; set; }
    public ShortcutAction Action { get; set; }
    public int Slot { get; set; }
    public bool Enabled { get; set; }=true;
    [JsonIgnore] public string Gesture => new KeyGesture(Key,Modifiers).ToString();
}
public sealed class KeyboardProfile
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public string Name { get; set; }="Standard";
    public List<ShortcutBinding> Bindings { get; set; }=new();
    public bool MediaKeysTagMode { get; set; }
    public override string ToString()=>Name;
}
public sealed class KeyboardSettings
{
    public bool FocusedEnabled { get; set; }=true;
    public bool GlobalEnabled { get; set; }
    public List<KeyboardProfile> Profiles { get; set; }=[new()];
    public Guid? ActiveProfileId { get; set; }
    [JsonIgnore] public KeyboardProfile ActiveProfile=>Profiles.FirstOrDefault(p=>p.Id==ActiveProfileId)??Profiles[0];
}
public static class KeyboardShortcuts
{
    public static string ActionLabel(ShortcutAction action)=>action switch
    {
        ShortcutAction.PlayPause=>"Player: Play / Pause",ShortcutAction.Next=>"Player: Nächster Track",ShortcutAction.Previous=>"Player: Vorheriger Track",
        ShortcutAction.FavoriteSlot=>"Player-Track zu Favoriten-Slot",ShortcutAction.ClubToggle=>"Player-Track: Club umschalten",ShortcutAction.ExtendedToggle=>"Player-Track: Extended umschalten",
        ShortcutAction.IntroToggle=>"Player-Track: Intro umschalten",ShortcutAction.MediaKeysModeToggle=>"Medientasten: Modus umschalten",_=>"Recorder: Übergang markieren"
    };
    public static bool IsModifier(Key key)=>key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
    public static string? Error(ShortcutBinding binding)
    {
        if(!Enum.IsDefined(binding.Key)||binding.Key is Key.None or Key.DeadCharProcessed or Key.ImeProcessed || IsModifier(binding.Key))return "Bitte eine Taste mit optionalen Zusatztasten wählen.";
        if(!Enum.IsDefined(binding.Action)||!Enum.IsDefined(binding.Scope))return "Unbekannte Aktion oder Geltungsbereich.";
        if((binding.Modifiers & ~(KeyModifiers.Control|KeyModifiers.Alt|KeyModifiers.Shift|KeyModifiers.Meta))!=0)return "Ungültige Zusatztasten.";
        if(binding.Key is Key.MediaPlayPause or Key.MediaNextTrack or Key.MediaPreviousTrack or Key.MediaStop)return "Für Medientasten den Modus Wiedergabe / Tags wählen. Er gilt auch im Hintergrund.";
        if(binding.Action==ShortcutAction.FavoriteSlot && binding.Slot is <0 or >63)return "Slot muss zwischen 1 und 64 liegen.";
        if(binding.Scope==ShortcutScope.Global)
        {
            if(binding.Modifiers.HasFlag(KeyModifiers.Meta)||binding.Key is Key.F12 or Key.PrintScreen)return "Diese Kombination ist für Windows bzw. Debugger reserviert.";
            var mods=binding.Modifiers;
            var count=(mods.HasFlag(KeyModifiers.Control)?1:0)+(mods.HasFlag(KeyModifiers.Alt)?1:0)+(mods.HasFlag(KeyModifiers.Shift)?1:0);
            if(count<2)return "Globale Hotkeys benötigen mindestens zwei Zusatztasten (Strg, Alt, Shift).";
        }
        return null;
    }
    public static void Validate(KeyboardSettings settings)
    {
        if(settings.Profiles.Count==0)throw new ArgumentException("Mindestens ein Tastaturprofil ist erforderlich.");
        if(settings.Profiles.Select(p=>p.Id).Distinct().Count()!=settings.Profiles.Count)throw new ArgumentException("Profil-IDs sind doppelt.");
        if(settings.Profiles.Select(p=>p.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=settings.Profiles.Count)throw new ArgumentException("Profilnamen müssen eindeutig sein.");
        foreach(var profile in settings.Profiles)
        {
            if(string.IsNullOrWhiteSpace(profile.Name))throw new ArgumentException("Profilname darf nicht leer sein.");
            var seen=new HashSet<(Key,KeyModifiers)>();
            foreach(var binding in profile.Bindings.Where(b=>b.Enabled))
            {
                if(Error(binding) is { } error)throw new ArgumentException(profile.Name+": "+error);
                if(!seen.Add((binding.Key,binding.Modifiers)))throw new ArgumentException(profile.Name+": "+binding.Gesture+" ist mehrfach belegt (auch zwischen Fokus und Global).");
            }
        }
    }
    public static ShortcutBinding? Match(KeyboardSettings settings,Key key,KeyModifiers modifiers,bool editing,bool active)
        => active && !editing && settings.FocusedEnabled ? settings.ActiveProfile.Bindings.FirstOrDefault(b=>b.Enabled&&b.Scope==ShortcutScope.Focused&&b.Key==key&&b.Modifiers==modifiers) : null;
}
