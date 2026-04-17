using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class BlockedConnectionAggregatorTests
{
    private readonly BlockedConnectionAggregator _aggregator = new();

    private static BlockedConnection Conn(string ip, int port, DateTime time) =>
        new(ip, port, time);

    private static Dictionary<string, IReadOnlyList<string>> DnsMap(
        params (string ip, string[] hostnames)[] entries) =>
        entries.ToDictionary(
            e => e.ip,
            e => (IReadOnlyList<string>)e.hostnames,
            StringComparer.OrdinalIgnoreCase);

    // --- AggregateByAddress ---

    [Fact]
    public void AggregateByAddress_GroupsByIp()
    {
        var t = DateTime.UtcNow;
        var connections = new List<BlockedConnection>
        {
            Conn("1.2.3.4", 443, t),
            Conn("1.2.3.4", 80, t.AddMinutes(-5)),
            Conn("5.6.7.8", 443, t.AddMinutes(-1)),
        };

        var rows = _aggregator.AggregateByAddress(connections);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Single(r => r.IpAddress == "1.2.3.4").HitCount);
        Assert.Equal(1, rows.Single(r => r.IpAddress == "5.6.7.8").HitCount);
    }

    [Fact]
    public void AggregateByAddress_FiltersLoopback()
    {
        var t = DateTime.UtcNow;
        var connections = new List<BlockedConnection>
        {
            Conn("1.2.3.4", 80, t),
            Conn("127.0.0.1", 80, t),
            Conn("::1", 80, t),
        };

        var rows = _aggregator.AggregateByAddress(connections);

        Assert.Single(rows);
        Assert.Equal("1.2.3.4", rows[0].IpAddress);
    }

    [Fact]
    public void AggregateByAddress_NonLoopbackIPv6_IncludedInResults()
    {
        var t = DateTime.UtcNow;
        var connections = new List<BlockedConnection>
        {
            Conn("2001:db8::1", 443, t),
        };

        var rows = _aggregator.AggregateByAddress(connections);

        Assert.Single(rows);
        Assert.Equal("2001:db8::1", rows[0].IpAddress);
        Assert.Equal(1, rows[0].HitCount);
        Assert.Contains(443, rows[0].Ports);
    }

    [Fact]
    public void AggregateByAddress_LastSeenIsMax()
    {
        var older = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var connections = new List<BlockedConnection>
        {
            Conn("1.2.3.4", 80, older),
            Conn("1.2.3.4", 443, newer),
        };

        var rows = _aggregator.AggregateByAddress(connections);

        Assert.Equal(newer, rows[0].LastSeen);
    }

    [Fact]
    public void AggregateByAddress_DistinctPortsSorted()
    {
        var t = DateTime.UtcNow;
        var connections = new List<BlockedConnection>
        {
            Conn("1.2.3.4", 443, t),
            Conn("1.2.3.4", 80, t),
            Conn("1.2.3.4", 443, t),
        };

        var rows = _aggregator.AggregateByAddress(connections);

        Assert.Equal([80, 443], rows[0].Ports);
    }

    [Fact]
    public void AggregateByAddress_OrderedByLastSeenDescending()
    {
        var t = DateTime.UtcNow;
        var connections = new List<BlockedConnection>
        {
            Conn("1.1.1.1", 80, t.AddMinutes(-5)),
            Conn("2.2.2.2", 80, t.AddMinutes(-3)),
            Conn("2.2.2.2", 443, t),
            Conn("3.3.3.3", 443, t.AddMinutes(-1)),
        };

        var rows = _aggregator.AggregateByAddress(connections);

        Assert.Equal("2.2.2.2", rows[0].IpAddress);
        Assert.Equal("3.3.3.3", rows[1].IpAddress);
        Assert.Equal("1.1.1.1", rows[2].IpAddress);
    }

    [Fact]
    public void AggregateByAddress_EmptyInput_ReturnsEmpty()
    {
        var rows = _aggregator.AggregateByAddress([]);
        Assert.Empty(rows);
    }

    // --- BuildAllowlistEntries ---

    [Fact]
    public void BuildAllowlistEntries_IpMode_ProducesIpEntries()
    {
        var rows = new List<BlockedConnectionRow>
        {
            new("1.2.3.4", 1, DateTime.UtcNow, [80]),
        };

        var entries = _aggregator.BuildAllowlistEntries(rows, isDomainMode: false, DnsMap());

        Assert.Single(entries);
        Assert.Equal("1.2.3.4", entries[0].Value);
        Assert.False(entries[0].IsDomain);
    }

    [Fact]
    public void BuildAllowlistEntries_DomainMode_UsesHostnameWhenAvailable()
    {
        var rows = new List<BlockedConnectionRow>
        {
            new("1.2.3.4", 1, DateTime.UtcNow, [443]),
        };

        var entries = _aggregator.BuildAllowlistEntries(rows, isDomainMode: true,
            DnsMap(("1.2.3.4", ["example.com"])));

        Assert.Single(entries);
        Assert.Equal("example.com", entries[0].Value);
        Assert.True(entries[0].IsDomain);
    }

    [Fact]
    public void BuildAllowlistEntries_DomainMode_MultipleHostnames_AddsOneEntryEach()
    {
        var rows = new List<BlockedConnectionRow>
        {
            new("1.2.3.4", 1, DateTime.UtcNow, [443]),
        };

        var entries = _aggregator.BuildAllowlistEntries(rows, isDomainMode: true,
            DnsMap(("1.2.3.4", ["a.example.com", "b.example.com"])));

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.True(e.IsDomain));
        Assert.Equal("a.example.com", entries[0].Value);
        Assert.Equal("b.example.com", entries[1].Value);
    }

    [Fact]
    public void BuildAllowlistEntries_DomainMode_FallsBackToIpWhenNoHostnames()
    {
        var rows = new List<BlockedConnectionRow>
        {
            new("1.2.3.4", 1, DateTime.UtcNow, [443]),
        };

        var entries = _aggregator.BuildAllowlistEntries(rows, isDomainMode: true,
            DnsMap(("1.2.3.4", [])));

        Assert.Equal("1.2.3.4", entries[0].Value);
        Assert.False(entries[0].IsDomain);
    }

    [Fact]
    public void BuildAllowlistEntries_DomainMode_FallsBackToIpWhenNotInDnsMap()
    {
        var rows = new List<BlockedConnectionRow>
        {
            new("1.2.3.4", 1, DateTime.UtcNow, [443]),
        };

        var entries = _aggregator.BuildAllowlistEntries(rows, isDomainMode: true, DnsMap());

        Assert.Equal("1.2.3.4", entries[0].Value);
        Assert.False(entries[0].IsDomain);
    }

    [Fact]
    public void BuildAllowlistEntries_MultipleRows_AllConverted()
    {
        var rows = new List<BlockedConnectionRow>
        {
            new("1.2.3.4", 2, DateTime.UtcNow, [80]),
            new("5.6.7.8", 1, DateTime.UtcNow, [443]),
        };

        var entries = _aggregator.BuildAllowlistEntries(rows, isDomainMode: true,
            DnsMap(("1.2.3.4", ["a.example.com"])));

        Assert.Equal(2, entries.Count);
        Assert.Equal("a.example.com", entries[0].Value);
        Assert.True(entries[0].IsDomain);
        Assert.Equal("5.6.7.8", entries[1].Value);
        Assert.False(entries[1].IsDomain);
    }
}