using System.Runtime.InteropServices;
using Avalonia.Input;
#if WINDOWS
using Avalonia.Win32.Input;
#endif
namespace Cratebase.App.Desktop.Services;

public sealed record HotkeyRegistration(ShortcutBinding Binding,bool Success,string Message);

/// <summary>Only registers explicit chords. No keyboard hook, key logging, or key injection.</summary>
public sealed class WindowsGlobalHotkeys(IntPtr hwnd) : IDisposable
{
    private readonly Dictionary<int,ShortcutBinding> _registered=new();
    private int _nextId=0x4000;
    public event Action<ShortcutBinding>? Pressed;
    public IReadOnlyList<HotkeyRegistration> Apply(IEnumerable<ShortcutBinding> bindings)
    {
        Dispose();
        var result=new List<HotkeyRegistration>();
        foreach(var binding in bindings.Where(b=>b.Enabled&&b.Scope==ShortcutScope.Global))
        {
            if(KeyboardShortcuts.Error(binding) is { } error) {result.Add(new(binding,false,error));continue;}
#if WINDOWS
            if(!OperatingSystem.IsWindows()||hwnd==IntPtr.Zero) {result.Add(new(binding,false,"Globale Hotkeys sind nur unter Windows verfügbar."));continue;}
            var key=KeyInterop.VirtualKeyFromKey(binding.Key);
            if(key==0) {result.Add(new(binding,false,"Diese Taste wird global nicht unterstützt."));continue;}
            var id=_nextId++;
            if(_nextId>=0xBFFF)_nextId=0x4000;
            uint modifiers=0x4000; // MOD_NOREPEAT: one action per physical press.
            if(binding.Modifiers.HasFlag(KeyModifiers.Alt))modifiers|=1;
            if(binding.Modifiers.HasFlag(KeyModifiers.Control))modifiers|=2;
            if(binding.Modifiers.HasFlag(KeyModifiers.Shift))modifiers|=4;
            if(RegisterHotKey(hwnd,id,modifiers,(uint)key)) {_registered[id]=binding;result.Add(new(binding,true,"Aktiv"));}
            else
            {
                var code=Marshal.GetLastWin32Error();
                result.Add(new(binding,false,code==1409?"Bereits von Windows oder einer anderen App registriert.":$"Registrierung fehlgeschlagen (Windows {code})."));
            }
#else
            result.Add(new(binding,false,"Globale Hotkeys sind nur unter Windows verfügbar."));
#endif
        }
        return result;
    }
    public bool HandleMessage(uint message,IntPtr id)
    {
        if(message!=0x0312||!_registered.TryGetValue(id.ToInt32(),out var binding))return false;
        Pressed?.Invoke(binding);return true;
    }
    public void Dispose()
    {
        if(OperatingSystem.IsWindows())foreach(var id in _registered.Keys)UnregisterHotKey(hwnd,id);
        _registered.Clear();
    }
    [DllImport("user32.dll",SetLastError=true)]private static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]private static extern bool UnregisterHotKey(IntPtr hwnd,int id);
}
