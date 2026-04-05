using System.Net;
using RunFence.Core.Models;

namespace RunFence.Firewall;

public record BlockedConnectionRow(
    string IpAddress,
    int HitCount,
    DateTime LastSeen,
    IReadOnlyList<int> Ports);

public class BlockedConnectionAggregator
{
    public List<BlockedConnectionRow> AggregateByAddress(List<BlockedConnection> connections)
    {
        return connections
            .Where(c => !(IPAddress.TryParse(c.DestAddress, out var addr) && IPAddress.IsLoopback(addr)))
            .GroupBy(c => c.DestAddress, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BlockedConnectionRow(
                IpAddress: g.Key,
                HitCount: g.Count(),
                LastSeen: g.Max(c => c.TimeStamp),
                Ports: g.Select(c => c.DestPort).Distinct().OrderBy(p => p).ToList()))
            .OrderByDescending(r => r.LastSeen)
            .ToList();
    }

    public List<FirewallAllowlistEntry> BuildAllowlistEntries(
        IEnumerable<BlockedConnectionRow> rows,
        bool isDomainMode,
        IReadOnlyDictionary<string, IReadOnlyList<string>> reverseDnsMap)
    {
        var entries = new List<FirewallAllowlistEntry>();
        foreach (var row in rows)
        {
            if (isDomainMode &&
                reverseDnsMap.TryGetValue(row.IpAddress, out var hostnames) &&
                hostnames.Count > 0)
            {
                entries.AddRange(hostnames.Select(hostname => new FirewallAllowlistEntry { Value = hostname, IsDomain = true }));
            }
            else
            {
                entries.Add(new FirewallAllowlistEntry { Value = row.IpAddress, IsDomain = false });
            }
        }

        return entries;
    }
}