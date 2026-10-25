using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Input;
using Cratebase.App.Desktop.Services;

internal static class KeyboardChecks
{
    public static void Run()
    {
        var settings=new KeyboardSettings();
        Check(!settings.GlobalEnabled && settings.ActiveProfile.Bindings.Count==0,"Keyboard starts with global hotkeys off and no reserved keys");
        var focused=new ShortcutBinding {Key=Key.F9,Modifiers=KeyModifiers.Control,Action=ShortcutAction.ClubToggle};
        settings.ActiveProfile.Bindings.Add(focused);
        Check(KeyboardShortcuts.Match(settings,Key.F9,KeyModifiers.Control,false,true)==focused,"Focused chord selects its action");
        Check(KeyboardShortcuts.Match(settings,Key.F9,KeyModifiers.Control,true,true)==null && KeyboardShortcuts.Match(settings,Key.F9,KeyModifiers.Control,false,false)==null,"Focused chords pause while editing or outside the app");
        settings.FocusedEnabled=false;
        Check(KeyboardShortcuts.Match(settings,Key.F9,KeyModifiers.Control,false,true)==null,"Focused shortcuts can be disabled independently");
        settings.FocusedEnabled=true;
        var global=focused with {Id=Guid.NewGuid(),Scope=ShortcutScope.Global,Key=Key.F24,Modifiers=KeyModifiers.Control|KeyModifiers.Alt|KeyModifiers.Shift};
        Check(KeyboardShortcuts.Error(global)==null && KeyboardShortcuts.Error(global with {Modifiers=KeyModifiers.None})!=null,"Global chords require modifiers and accept explicit rare combinations");
        Check(KeyboardShortcuts.Error(global with {Key=Key.F12})!=null && KeyboardShortcuts.Error(global with {Modifiers=KeyModifiers.Meta|KeyModifiers.Control})!=null,"Reserved debugger and Windows combinations are rejected");
        Check(KeyboardShortcuts.Error(focused with {Key=Key.MediaPlayPause})!=null,"Dedicated media keys keep the Windows media integration");
        settings.ActiveProfile.Bindings.Add(focused with {Id=Guid.NewGuid(),Scope=ShortcutScope.Global,Modifiers=KeyModifiers.Control|KeyModifiers.Alt});
        settings.ActiveProfile.Bindings.Add(global);
        settings.GlobalEnabled=true;
        var alternative=new KeyboardProfile {Name="Gaming",MediaKeysTagMode=true};settings.Profiles.Add(alternative);settings.ActiveProfileId=alternative.Id;
        var json=JsonSerializer.Serialize(settings);var restored=JsonSerializer.Deserialize<KeyboardSettings>(json)!;KeyboardShortcuts.Validate(restored);
        Check(restored.ActiveProfile.Name=="Gaming" && restored.GlobalEnabled && restored.Profiles[0].Bindings.Count==3,"Profiles, scopes, keys and activation survive serialization");
        Check(restored.ActiveProfile.MediaKeysTagMode && !restored.Profiles[0].MediaKeysTagMode,"Each keyboard profile persists its own media-key mode");
        Check(KeyboardShortcuts.Match(restored,Key.F9,KeyModifiers.Control,false,true)==null,"Switching profile releases the previous local action");
        var duplicate=global with {Id=Guid.NewGuid(),Scope=ShortcutScope.Focused};settings.Profiles[0].Bindings.Add(duplicate);
        try {KeyboardShortcuts.Validate(settings);throw new Exception("Duplicate chord accepted");}catch(ArgumentException){Console.WriteLine("PASS: Duplicate chords across scopes are reported");}
        settings.Profiles[0].Bindings.Remove(duplicate);
        if(OperatingSystem.IsWindows())NativeRegistration(global);
    }
    private static void NativeRegistration(ShortcutBinding binding)
    {
        var firstWindow=CreateWindowExW(0,"STATIC","Cratebase keyboard test 1",0,0,0,1,1,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        var secondWindow=CreateWindowExW(0,"STATIC","Cratebase keyboard test 2",0,0,0,1,1,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(firstWindow==IntPtr.Zero||secondWindow==IntPtr.Zero)throw new Exception("Test window creation failed");
        try
        {
            using var first=new WindowsGlobalHotkeys(firstWindow);using var second=new WindowsGlobalHotkeys(secondWindow);
            var result=first.Apply([binding]).Single();
            Check(result.Success,"Windows registers the explicitly enabled global chord");
            Check(!second.Apply([binding]).Single().Success,"A conflicting registration is reported without taking it over");
            var calls=0;first.Pressed+=b=> {if(b.Id==binding.Id)calls++;};
            Check(first.HandleMessage(0x312,new IntPtr(0x4000))&&calls==1,"WM_HOTKEY dispatches only the registered action");
            first.Apply([]);
            Check(!first.HandleMessage(0x312,new IntPtr(0x4000)),"Disabling or switching profiles invalidates stale hotkey messages");
            Check(second.Apply([binding]).Single().Success,"Disabling global shortcuts releases the chord for other apps");
            second.Dispose();Check(first.Apply([binding]).Single().Success,"Closing a hotkey service releases every registration");
        }
        finally {DestroyWindow(firstWindow);DestroyWindow(secondWindow);}
    }
    private static void Check(bool condition,string text) {if(!condition)throw new Exception("FAIL: "+text);Console.WriteLine("PASS: "+text);}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CreateWindowExW(int exStyle,string className,string title,int style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(IntPtr hwnd);
}
