using Avalonia.Controls;
using Avalonia.Platform;

namespace Cratebase.App.Desktop.Controls;

/// <summary>Avalonia retains/reparents the native child when moving between windows.
/// LibVLC keeps rendering to that same child, so docking need not restart playback.</summary>
public sealed class ReviewVideoSurface : NativeControlHost
{
    public IntPtr VideoHandle {get;private set;}
    public event Action<IntPtr>? HandleChanged;
    private TaskCompletionSource<IntPtr> _ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<IntPtr> WaitForHandleAsync(CancellationToken ct)=>VideoHandle!=IntPtr.Zero?Task.FromResult(VideoHandle):_ready.Task.WaitAsync(ct);
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var child=base.CreateNativeControlCore(parent);
        if(child.HandleDescriptor=="HWND"){VideoHandle=child.Handle;_ready.TrySetResult(child.Handle);HandleChanged?.Invoke(child.Handle);}
        return child;
    }
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        VideoHandle=IntPtr.Zero;HandleChanged?.Invoke(IntPtr.Zero);_ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        base.DestroyNativeControlCore(control);
    }
}
