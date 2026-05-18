using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Firewall;

public class EphemeralPortSnapshotReader(
    ILoggingService log)
    : IEphemeralPortSnapshotReader
{
    private readonly EphemeralPortSnapshotParser _parser = new();

    public IReadOnlyList<(int Port, int Pid)> ReadListeningPortsSnapshot(bool isTcp, bool isIPv6)
    {
        var af = isIPv6 ? IphlpapiNative.AF_INET6 : IphlpapiNative.AF_INET;
        var size = 0;
        var buf = IntPtr.Zero;

        try
        {
            int rc;
            if (isTcp)
                rc = IphlpapiNative.GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, IphlpapiNative.TcpTableClass.OwnerPidListener, 0);
            else
                rc = IphlpapiNative.GetExtendedUdpTable(IntPtr.Zero, ref size, false, af, IphlpapiNative.UdpTableClass.OwnerPid, 0);

            if (rc != IphlpapiNative.ERROR_INSUFFICIENT_BUFFER)
            {
                if (rc != IphlpapiNative.ERROR_INVALID_PARAMETER)
                    log.Warn($"WfpEphemeralPortScanner: {(isTcp ? "TCP" : "UDP")}/{(isIPv6 ? "IPv6" : "IPv4")} table query failed (0x{rc:X8})");
                return [];
            }

            buf = Marshal.AllocHGlobal(size);

            if (isTcp)
                rc = IphlpapiNative.GetExtendedTcpTable(buf, ref size, false, af, IphlpapiNative.TcpTableClass.OwnerPidListener, 0);
            else
                rc = IphlpapiNative.GetExtendedUdpTable(buf, ref size, false, af, IphlpapiNative.UdpTableClass.OwnerPid, 0);

            if (rc == IphlpapiNative.ERROR_INSUFFICIENT_BUFFER)
            {
                Marshal.FreeHGlobal(buf);
                buf = Marshal.AllocHGlobal(size);

                if (isTcp)
                    rc = IphlpapiNative.GetExtendedTcpTable(buf, ref size, false, af, IphlpapiNative.TcpTableClass.OwnerPidListener, 0);
                else
                    rc = IphlpapiNative.GetExtendedUdpTable(buf, ref size, false, af, IphlpapiNative.UdpTableClass.OwnerPid, 0);
            }

            if (rc != IphlpapiNative.ERROR_SUCCESS)
            {
                log.Warn($"WfpEphemeralPortScanner: {(isTcp ? "TCP" : "UDP")}/{(isIPv6 ? "IPv6" : "IPv4")} table query failed (0x{rc:X8})");
                return [];
            }

            var tableBytes = new byte[size];
            Marshal.Copy(buf, tableBytes, 0, size);
            return _parser.Parse(tableBytes, GetTableKind(isTcp, isIPv6));
        }
        finally
        {
            if (buf != IntPtr.Zero)
                Marshal.FreeHGlobal(buf);
        }
    }

    private static EphemeralPortTableKind GetTableKind(bool isTcp, bool isIPv6) =>
        (isTcp, isIPv6) switch
        {
            (true, false) => EphemeralPortTableKind.TcpIpv4,
            (true, true) => EphemeralPortTableKind.TcpIpv6,
            (false, false) => EphemeralPortTableKind.UdpIpv4,
            _ => EphemeralPortTableKind.UdpIpv6
        };
}
