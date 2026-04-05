using System.Runtime.InteropServices;
using RunFence.Core;

// ReSharper disable UnusedMember.Global

namespace RunFence.MediaBridge;

/// <summary>
/// Checks Core Audio sessions via COM to determine whether any audio session
/// owned by a given user SID is currently active (playing).
/// </summary>
public class CoreAudioSessionChecker : ICoreAudioSessionChecker
{
    private static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    private const int AudioSessionStateActive = 1;
    private const uint ClsctxAll = 0x17;

    /// <summary>
    /// Returns true if any audio session owned by <paramref name="interactiveSid"/>
    /// is currently in the Active state on the default audio render endpoint.
    /// Returns false on any COM failure (device unavailable, no sessions, etc.).
    /// </summary>
    public bool IsAnySessionActive(string interactiveSid)
    {
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(ClsidMmDeviceEnumerator)
                                 ?? throw new COMException("MMDeviceEnumerator CLSID not found.");
            enumerator = (IMMDeviceEnumerator)(Activator.CreateInstance(enumeratorType)
                                               ?? throw new COMException("Failed to create MMDeviceEnumerator."));

            IMMDevice? device = null;
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(0, 0, out device) != 0 || device == null)
                    return false;

                var iid = IidAudioSessionManager2;
                if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var managerObj) != 0
                    || managerObj == null)
                    return false;

                var manager = (IAudioSessionManager2)managerObj;
                IAudioSessionEnumerator? sessionEnum = null;
                try
                {
                    if (manager.GetSessionEnumerator(out sessionEnum) != 0 || sessionEnum == null)
                        return false;

                    if (sessionEnum.GetCount(out int count) != 0)
                        return false;

                    for (int i = 0; i < count; i++)
                    {
                        IAudioSessionControl? sessionControl = null;
                        try
                        {
                            if (sessionEnum.GetSession(i, out sessionControl) != 0 || sessionControl == null)
                                continue;

                            var sc2 = (IAudioSessionControl2)sessionControl;
                            if (sc2.GetProcessId(out uint pid) != 0)
                                continue;
                            if (sc2.GetState(out int state) != 0)
                                continue;
                            if (state != AudioSessionStateActive)
                                continue;

                            var ownerSid = NativeTokenHelper.TryGetProcessOwnerSid(pid);
                            if (ownerSid != null && string.Equals(ownerSid.Value, interactiveSid,
                                    StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                        catch
                        {
                            /* skip inaccessible session */
                        }
                        finally
                        {
                            if (sessionControl != null)
                                Marshal.ReleaseComObject(sessionControl);
                        }
                    }
                }
                finally
                {
                    if (sessionEnum != null)
                        Marshal.ReleaseComObject(sessionEnum);
                    Marshal.ReleaseComObject(manager);
                }
            }
            finally
            {
                if (device != null)
                    Marshal.ReleaseComObject(device);
            }
        }
        catch
        {
            /* audio device unavailable or COM failure — treat as no active sessions */
        }
        finally
        {
            if (enumerator != null)
                Marshal.ReleaseComObject(enumerator);
        }

        return false;
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint deviceStateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        // IAudioSessionManager methods (base interface, must be declared for correct vtable layout)
        [PreserveSig] int GetAudioSessionControl(IntPtr audioSessionGuid, uint streamFlags, out IntPtr sessionControl);

        [PreserveSig] int GetSimpleAudioVolume(IntPtr audioSessionGuid, uint crossProcessSession, out IntPtr audioVolume);

        // IAudioSessionManager2 methods
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        [PreserveSig] int RegisterSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionID, IntPtr duckNotification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int sessionCount);
        [PreserveSig] int GetSession(int sessionCount, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingParam);
        [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notify);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notify);
    }

    // IAudioSessionControl2 extends IAudioSessionControl — inherited methods must be listed first
    // to match the COM vtable layout.
    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl methods
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingParam);
        [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notify);

        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notify);

        // IAudioSessionControl2 methods
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
        [PreserveSig] int GetProcessId(out uint retVal);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }
}