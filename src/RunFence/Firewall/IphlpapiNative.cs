using System.Runtime.InteropServices;

namespace RunFence.Firewall;

internal static class IphlpapiNative
{
    // No SetLastError: iphlpapi functions return error codes directly, not via GetLastError.
    [DllImport("iphlpapi.dll")]
    public static extern int GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize,
        bool bOrder, int ulAf, TcpTableClass TableClass, int Reserved);

    [DllImport("iphlpapi.dll")]
    public static extern int GetExtendedUdpTable(IntPtr pUdpTable, ref int pdwSize,
        bool bOrder, int ulAf, UdpTableClass TableClass, int Reserved);

    public const int ERROR_SUCCESS = 0;
    public const int AF_INET = 2;
    public const int AF_INET6 = 23;
    public const int ERROR_INVALID_PARAMETER = 87;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    // TCP_TABLE_OWNER_PID_LISTENER (3): only sockets in LISTEN state, with owning PID
    public enum TcpTableClass { OwnerPidListener = 3 }

    // UDP_TABLE_OWNER_PID: all bound UDP sockets (UDP has no LISTEN state)
    public enum UdpTableClass { OwnerPid = 1 }
}
