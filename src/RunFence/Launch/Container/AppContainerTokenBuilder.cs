using System.ComponentModel;
using RunFence.Core;

namespace RunFence.Launch.Container;

public class AppContainerTokenBuilder(
    ILoggingService log,
    IAppContainerTokenNativeApi nativeApi)
    : IAppContainerTokenBuilder
{
    public AppContainerLaunchTokenContext Build(
        IntPtr explorerToken,
        string containerSid,
        IReadOnlyList<string>? capabilities)
    {
        IntPtr duplicatedExplorerToken = IntPtr.Zero;
        IntPtr appContainerToken = IntPtr.Zero;
        IntPtr containerSidPointer = IntPtr.Zero;
        IntPtr capabilityArrayPointer = IntPtr.Zero;
        var capabilitySidPointers = new List<IntPtr>();

        try
        {
            containerSidPointer = nativeApi.ConvertRequiredStringSidToSid(containerSid);
            var (capabilityCount, marshaledCapabilityArray) = MarshalCapabilitySids(capabilities, capabilitySidPointers);
            capabilityArrayPointer = marshaledCapabilityArray;

            duplicatedExplorerToken = nativeApi.DuplicateToken(explorerToken);

            var securityCapabilities = new AppContainerProcessLauncherNative.SECURITY_CAPABILITIES
            {
                AppContainerSid = containerSidPointer,
                Capabilities = capabilityArrayPointer,
                CapabilityCount = capabilityCount,
                Reserved = 0
            };
            try
            {
                appContainerToken = nativeApi.CreateAppContainerToken(duplicatedExplorerToken, ref securityCapabilities);
            }
            catch (Win32Exception ex)
            {
                var nativeMessage = new Win32Exception(ex.NativeErrorCode).Message;
                log.Error($"AppContainerProcessLauncher: CreateAppContainerToken failed - Win32 error {ex.NativeErrorCode} (0x{ex.NativeErrorCode:X8}): {nativeMessage}");
                throw;
            }

            var interactiveUserSid = nativeApi.GetRequiredTokenSidValue(explorerToken);
            nativeApi.SetRestrictiveDefaultDacl(appContainerToken, containerSid, interactiveUserSid);

            TryLogNamedObjectPath(appContainerToken);

            return new AppContainerLaunchTokenContext(
                duplicatedExplorerToken,
                appContainerToken,
                interactiveUserSid,
                capabilitySidPointers.ToArray(),
                capabilityArrayPointer,
                containerSidPointer,
                nativeApi.LocalFree,
                nativeApi.FreeCapabilityArray,
                nativeApi.CloseHandle);
        }
        catch
        {
            foreach (var capabilitySidPointer in capabilitySidPointers)
            {
                if (capabilitySidPointer != IntPtr.Zero)
                    nativeApi.LocalFree(capabilitySidPointer);
            }

            if (capabilityArrayPointer != IntPtr.Zero)
                nativeApi.FreeCapabilityArray(capabilityArrayPointer);
            if (containerSidPointer != IntPtr.Zero)
                nativeApi.LocalFree(containerSidPointer);
            if (appContainerToken != IntPtr.Zero)
                nativeApi.CloseHandle(appContainerToken);
            if (duplicatedExplorerToken != IntPtr.Zero)
                nativeApi.CloseHandle(duplicatedExplorerToken);
            throw;
        }
    }

    private (uint CapabilityCount, IntPtr CapabilityArrayPointer) MarshalCapabilitySids(
        IReadOnlyList<string>? capabilities,
        ICollection<IntPtr> capabilitySidPointers)
    {
        if (capabilities is not { Count: > 0 })
            return (0u, IntPtr.Zero);

        foreach (var capabilitySid in capabilities)
        {
            if (nativeApi.TryConvertStringSidToSid(capabilitySid, out var sidPointer, out _))
            {
                capabilitySidPointers.Add(sidPointer);
            }
            else
            {
                log.Warn($"AppContainerProcessLauncher: Could not convert capability SID '{capabilitySid}', skipping");
            }
        }

        if (capabilitySidPointers.Count == 0)
            return (0u, IntPtr.Zero);

        return ((uint)capabilitySidPointers.Count, nativeApi.AllocateCapabilityArray(capabilitySidPointers.ToArray()));
    }

    private void TryLogNamedObjectPath(IntPtr appContainerToken)
    {
        try
        {
            if (nativeApi.TryGetAppContainerNamedObjectPath(appContainerToken, out var path, out var errorCode))
                log.Info($"AppContainerProcessLauncher: Named object path: {path}");
            else
                log.Warn($"AppContainerProcessLauncher: GetAppContainerNamedObjectPath failed (error {errorCode}) - named objects may not work");
        }
        catch (Exception ex)
        {
            log.Warn($"AppContainerProcessLauncher: Named object path check failed: {ex.Message}");
        }
    }
}
