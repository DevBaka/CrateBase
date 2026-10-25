using System.Runtime.InteropServices;
namespace Cratebase.App.Desktop.Services;

public sealed record MidiBinding(int Slot, int Channel, int Message, int Number);
public sealed class WindowsMidiInput : IDisposable
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct Caps { public ushort Manufacturer,Product; public uint Version; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name; public uint Support; }
    private delegate void MidiCallback(IntPtr handle,uint message,IntPtr instance,IntPtr param1,IntPtr param2);
    [DllImport("winmm.dll")] private static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll",CharSet=CharSet.Unicode)] private static extern uint midiInGetDevCapsW(UIntPtr id,out Caps caps,uint size);
    [DllImport("winmm.dll")] private static extern uint midiInOpen(out IntPtr handle,uint device,MidiCallback callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")] private static extern uint midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInClose(IntPtr handle);
    private IntPtr _handle;
    private readonly MidiCallback _callback;
    private readonly HashSet<(int,int,int)> _pressed=new();
    public event Action<int,int,int>? Pressed;
    public WindowsMidiInput() { _callback=Callback; }
    public static IReadOnlyList<string> Devices()
    {
        if(!OperatingSystem.IsWindows()) return [];
        var result=new List<string>();
        for(uint i=0;i<midiInGetNumDevs();i++) { midiInGetDevCapsW((UIntPtr)i,out var caps,(uint)Marshal.SizeOf<Caps>()); result.Add(caps.Name); }
        return result;
    }
    public void Open(int device)
    {
        Dispose(); _pressed.Clear();
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("MIDI wird in diesem Prototyp unter Windows unterstützt.");
        var error=midiInOpen(out _handle,(uint)device,_callback,IntPtr.Zero,0x30000);
        if(error!=0) throw new IOException($"MIDI-Gerät nicht verfügbar (WinMM {error}).");
        if(midiInStart(_handle)!=0) { Dispose(); throw new IOException("MIDI-Gerät konnte nicht gestartet werden."); }
    }
    private void Callback(IntPtr handle,uint message,IntPtr instance,IntPtr param1,IntPtr param2)
    {
        if(message!=0x3C3) return;
        var data=param1.ToInt64(); var status=(int)(data&255); var kind=status&0xF0; var channel=status&15; var number=(int)((data>>8)&127); var value=(int)((data>>16)&127);
        if(kind is not (0x80 or 0x90 or 0xB0)) return;
        var key=(channel,kind==0x80 ? 0x90 : kind,number);
        var down=kind==0x90 ? value>0 : kind==0xB0 && value>=64;
        if(down) { if(_pressed.Add(key)) Pressed?.Invoke(key.Item1,key.Item2,key.Item3); }
        else _pressed.Remove(key);
    }
    public void Dispose() { if(_handle==IntPtr.Zero) return; midiInStop(_handle); midiInReset(_handle); midiInClose(_handle); _handle=IntPtr.Zero; }
}
