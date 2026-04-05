using System.Runtime.InteropServices;

namespace RunFence.Launch.Container;

/// <summary>
/// Shared P/Invoke declarations used by both AppContainerService and AppContainerLauncher.
/// </summary>
public static class AppContainerNative
{
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ImpersonateLoggedOnUser(IntPtr hToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool RevertToSelf();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ConvertSidToStringSid(IntPtr pSid, out IntPtr pStringSid);

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int CreateAppContainerProfile(
        string pszAppContainerName, string pszDisplayName, string pszDescription,
        IntPtr pCapabilities, uint dwCapabilityCount, out IntPtr ppSidAppContainerSid);

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int DeriveAppContainerSidFromAppContainerName(
        string pszAppContainerName, out IntPtr ppsidAppContainerSid);

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int DeleteAppContainerProfile(string pszAppContainerName);
}