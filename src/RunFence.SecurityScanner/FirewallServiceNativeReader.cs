using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

public interface IFirewallServiceNativeReader
{
    (bool IsDisabled, bool IsStopped)? GetWindowsFirewallServiceState();
}

public class FirewallServiceNativeReader : IFirewallServiceNativeReader
{
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint OpenSCManager(string? machineName, string? databaseName, uint dwAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint OpenService(nint hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(nint hService, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(nint hSCObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    private const uint SERVICE_RUNNING = 4;
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_STATUS = 0x0004;

    public (bool IsDisabled, bool IsStopped)? GetWindowsFirewallServiceState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\MpsSvc");
            if (key == null)
                return null;
            bool isDisabled = key.GetValue("Start") is 4;

            bool isStopped = false;
            var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm != nint.Zero)
            {
                try
                {
                    var svc = OpenService(scm, "MpsSvc", SERVICE_QUERY_STATUS);
                    if (svc != nint.Zero)
                    {
                        try
                        {
                            var status = new SERVICE_STATUS();
                            if (QueryServiceStatus(svc, ref status))
                                isStopped = status.dwCurrentState != SERVICE_RUNNING;
                        }
                        finally
                        {
                            CloseServiceHandle(svc);
                        }
                    }
                }
                finally
                {
                    CloseServiceHandle(scm);
                }
            }

            return (isDisabled, isStopped);
        }
        catch
        {
            return null;
        }
    }
}
