namespace RunFence.Firewall;

public interface IDnsResolver
{
    Task<IReadOnlyList<string>> ResolveAsync(string hostname);
    Task<IReadOnlyList<string>> ResolveReverseAsync(string ipAddress);
}

public interface INetworkInterfaceInfoProvider
{
    IReadOnlyList<string> GetDnsServerAddresses();
    IReadOnlyList<string> GetLocalAddresses();
}