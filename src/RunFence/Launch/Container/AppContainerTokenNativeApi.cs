using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using RunFence.Infrastructure;

namespace RunFence.Launch.Container;

public class AppContainerTokenNativeApi : IAppContainerTokenNativeApi
{
    public IntPtr ConvertRequiredStringSidToSid(string sid)
    {
        if (!ProcessNative.ConvertStringSidToSid(sid, out var sidPointer))
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new Win32Exception(errorCode, $"ConvertStringSidToSid failed for SID '{sid}'");
        }

        return sidPointer;
    }

    public bool TryConvertStringSidToSid(string sid, out IntPtr pointer, out int errorCode)
    {
        var converted = ProcessNative.ConvertStringSidToSid(sid, out pointer);
        errorCode = converted ? 0 : Marshal.GetLastWin32Error();
        return converted;
    }

    public void LocalFree(IntPtr pointer)
        => ProcessNative.LocalFree(pointer);

    public IntPtr DuplicateToken(IntPtr token)
        => NativeTokenAcquisition.DuplicateToken(token);

    public IntPtr CreateAppContainerToken(
        IntPtr duplicatedExplorerToken,
        ref AppContainerProcessLauncherNative.SECURITY_CAPABILITIES capabilities)
    {
        if (!AppContainerProcessLauncherNative.CreateAppContainerToken(
                duplicatedExplorerToken,
                ref capabilities,
                out var appContainerToken))
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new Win32Exception(errorCode, "CreateAppContainerToken failed");
        }

        return appContainerToken;
    }

    public void SetRestrictiveDefaultDacl(IntPtr appContainerToken, string containerSid, string interactiveUserSid)
        => NativeTokenAcquisition.SetRestrictiveDefaultDacl(appContainerToken, containerSid, interactiveUserSid);

    public string GetRequiredTokenSidValue(IntPtr token)
    {
        ProcessNative.GetTokenInformation(token, ProcessNative.TokenUser, IntPtr.Zero, 0, out var needed);
        if (needed <= 0)
            throw new InvalidOperationException("Interactive user SID is unavailable from the explorer token.");

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ProcessNative.GetTokenInformation(token, ProcessNative.TokenUser, buffer, needed, out _))
                throw new InvalidOperationException("Interactive user SID is unavailable from the explorer token.");

            return new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public IntPtr AllocateCapabilityArray(IReadOnlyList<IntPtr> capabilitySidPointers)
    {
        if (capabilitySidPointers.Count == 0)
            return IntPtr.Zero;

        var elementSize = Marshal.SizeOf<ProcessLaunchNative.SID_AND_ATTRIBUTES>();
        var capabilityArrayPointer = Marshal.AllocHGlobal(elementSize * capabilitySidPointers.Count);
        for (var index = 0; index < capabilitySidPointers.Count; index++)
        {
            var item = new ProcessLaunchNative.SID_AND_ATTRIBUTES
            {
                Sid = capabilitySidPointers[index],
                Attributes = AppContainerProcessLauncherNative.SE_GROUP_ENABLED
            };
            Marshal.StructureToPtr(item, IntPtr.Add(capabilityArrayPointer, index * elementSize), false);
        }

        return capabilityArrayPointer;
    }

    public void FreeCapabilityArray(IntPtr pointer)
        => Marshal.FreeHGlobal(pointer);

    public bool TryGetAppContainerNamedObjectPath(IntPtr appContainerToken, out string path, out int errorCode)
    {
        var pathBuffer = new StringBuilder(512);
        var succeeded = AppContainerProcessLauncherNative.GetAppContainerNamedObjectPath(
            appContainerToken,
            IntPtr.Zero,
            (uint)pathBuffer.Capacity,
            pathBuffer,
            out _);
        errorCode = succeeded ? 0 : Marshal.GetLastWin32Error();
        path = succeeded ? pathBuffer.ToString() : string.Empty;
        return succeeded;
    }

    public void CloseHandle(IntPtr handle)
        => ProcessNative.CloseHandle(handle);
}
