namespace RunFence.Firewall;

public interface INetworkInterfaceInfoProvider
{
    IReadOnlyList<string> GetDnsServerAddresses();
    IReadOnlyList<string> GetLocalAddresses();
}
