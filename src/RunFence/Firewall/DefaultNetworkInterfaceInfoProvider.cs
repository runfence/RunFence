using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RunFence.Firewall;

public class DefaultNetworkInterfaceInfoProvider : INetworkInterfaceInfoProvider
{
    public IReadOnlyList<string> GetDnsServerAddresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                var props = ni.GetIPProperties();
                foreach (var dns in props.DnsAddresses)
                {
                    if (dns.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                        result.Add(FormatAddress(dns));
                }
            }
        }
        catch
        {
        }

        return result.Distinct().ToList();
    }

    public IReadOnlyList<string> GetLocalAddresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                var props = ni.GetIPProperties();
                foreach (var unicast in props.UnicastAddresses)
                {
                    var addr = unicast.Address;
                    if (addr.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
                        && !IPAddress.IsLoopback(addr))
                        result.Add(FormatAddress(addr));
                }
            }
        }
        catch
        {
        }

        return result.Distinct().ToList();
    }

    private static string FormatAddress(IPAddress addr) =>
        addr.AddressFamily == AddressFamily.InterNetworkV6 && addr.ScopeId != 0
            ? new IPAddress(addr.GetAddressBytes()).ToString()
            : addr.ToString();
}