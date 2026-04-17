using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.Launch.Container;

/// <summary>
/// Implements <see cref="IAppContainerSidProvider"/> by calling the native
/// <c>DeriveAppContainerSidFromAppContainerName</c> P/Invoke and managing the pointer lifecycle
/// internally. The native SID pointer is never exposed to callers.
/// </summary>
public class AppContainerSidProvider : IAppContainerSidProvider
{
    public string GetSidString(string containerName)
    {
        var hr = AppContainerNative.DeriveAppContainerSidFromAppContainerName(containerName, out var pSid);
        if (hr != 0 || pSid == IntPtr.Zero)
            throw new InvalidOperationException(
                $"DeriveAppContainerSidFromAppContainerName failed with HRESULT 0x{hr:X8} for '{containerName}'");

        IntPtr pStringSid = IntPtr.Zero;
        try
        {
            if (!AppContainerNative.ConvertSidToStringSid(pSid, out pStringSid))
            {
                var err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err);
            }

            return Marshal.PtrToStringUni(pStringSid) ?? throw new InvalidOperationException("Null SID string");
        }
        finally
        {
            if (pStringSid != IntPtr.Zero)
                ProcessNative.LocalFree(pStringSid);
            ProcessNative.LocalFree(pSid);
        }
    }
}
