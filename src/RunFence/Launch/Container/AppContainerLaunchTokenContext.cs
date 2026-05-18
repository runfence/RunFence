using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.Launch.Container;

public sealed class AppContainerLaunchTokenContext : IDisposable
{
    private readonly IReadOnlyList<IntPtr> _capabilitySidPointers;
    private readonly Action<IntPtr> _capabilityArrayFree;
    private readonly Action<IntPtr> _closeHandle;
    private readonly Action<IntPtr> _localFree;
    private int _disposed;

    public AppContainerLaunchTokenContext(
        IntPtr duplicatedExplorerToken,
        IntPtr appContainerToken,
        string interactiveUserSid,
        IReadOnlyList<IntPtr> capabilitySidPointers,
        IntPtr capabilityArrayPointer,
        IntPtr containerSidPointer)
        : this(
            duplicatedExplorerToken,
            appContainerToken,
            interactiveUserSid,
            capabilitySidPointers,
            capabilityArrayPointer,
            containerSidPointer,
            pointer => ProcessNative.LocalFree(pointer),
            pointer => Marshal.FreeHGlobal(pointer),
            handle => ProcessNative.CloseHandle(handle))
    {
    }

    internal AppContainerLaunchTokenContext(
        IntPtr duplicatedExplorerToken,
        IntPtr appContainerToken,
        string interactiveUserSid,
        IReadOnlyList<IntPtr> capabilitySidPointers,
        IntPtr capabilityArrayPointer,
        IntPtr containerSidPointer,
        Action<IntPtr> localFree,
        Action<IntPtr> capabilityArrayFree,
        Action<IntPtr> closeHandle)
    {
        DuplicatedExplorerToken = duplicatedExplorerToken;
        AppContainerToken = appContainerToken;
        InteractiveUserSid = interactiveUserSid;
        _capabilitySidPointers = capabilitySidPointers;
        CapabilitySidPointers = capabilitySidPointers;
        CapabilityArrayPointer = capabilityArrayPointer;
        ContainerSidPointer = containerSidPointer;
        _localFree = localFree;
        _capabilityArrayFree = capabilityArrayFree;
        _closeHandle = closeHandle;
    }

    public IntPtr DuplicatedExplorerToken { get; }

    public IntPtr AppContainerToken { get; }

    public string InteractiveUserSid { get; }

    public IReadOnlyList<IntPtr> CapabilitySidPointers { get; }

    public IntPtr CapabilityArrayPointer { get; }

    public IntPtr ContainerSidPointer { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var capabilitySidPointer in _capabilitySidPointers)
        {
            if (capabilitySidPointer != IntPtr.Zero)
                _localFree(capabilitySidPointer);
        }

        if (CapabilityArrayPointer != IntPtr.Zero)
            _capabilityArrayFree(CapabilityArrayPointer);
        if (ContainerSidPointer != IntPtr.Zero)
            _localFree(ContainerSidPointer);
        if (AppContainerToken != IntPtr.Zero)
            _closeHandle(AppContainerToken);
        if (DuplicatedExplorerToken != IntPtr.Zero)
            _closeHandle(DuplicatedExplorerToken);
    }
}
