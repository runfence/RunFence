using System.Net;

namespace RunFence.Firewall;

public class DefaultDnsResolver : IDnsResolver
{
    public async Task<IReadOnlyList<string>> ResolveAsync(string hostname)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname).ConfigureAwait(false);
            return addresses.Select(a => a.ToString()).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task<IReadOnlyList<string>> ResolveReverseAsync(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var addr))
            return Array.Empty<string>();
        try
        {
            var entry = await Dns.GetHostEntryAsync(addr).ConfigureAwait(false);
            var names = new[] { entry.HostName }
                .Concat(entry.Aliases)
                .Where(n => !string.IsNullOrEmpty(n) &&
                            !string.Equals(n, ipAddress, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return names.Count > 0 ? names : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}