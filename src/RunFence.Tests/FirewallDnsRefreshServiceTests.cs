using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class FirewallDnsRefreshServiceTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string Username = "alice";

    private readonly Mock<IFirewallService> _firewallService = new();
    private readonly Mock<IFirewallNetworkInfo> _networkInfo = new();
    private readonly Mock<ILoggingService> _log = new();

    public FirewallDnsRefreshServiceTests()
    {
        _networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns([]);
    }

    private FirewallDnsRefreshService BuildService(AppDatabase? database = null) =>
        new(_firewallService.Object, _log.Object,
            new LambdaDatabaseProvider(() => database ?? new AppDatabase()),
            new InlineUiThreadInvoker(action => action()),
            _networkInfo.Object);

    private static AppDatabase BuildDatabase(
        bool allowInternet = false,
        bool addDomainEntry = false,
        bool addIpEntry = false,
        bool allowLocalhost = true)
    {
        var db = new AppDatabase
        {
            SidNames =
            {
                [Sid] = Username
            }
        };
        var settings = new FirewallAccountSettings
        {
            AllowInternet = allowInternet,
            AllowLocalhost = allowLocalhost,
            AllowLan = true
        };
        if (addDomainEntry)
            settings.Allowlist.Add(new FirewallAllowlistEntry { Value = "example.com", IsDomain = true });
        if (addIpEntry)
            settings.Allowlist.Add(new FirewallAllowlistEntry { Value = "10.0.0.1", IsDomain = false });
        db.GetOrCreateAccount(Sid).Firewall = settings;
        return db;
    }

    [Fact]
    public void ProcessDnsRefresh_NoDomainEntries_DoesNotRefresh()
    {
        // Arrange — allowlist has only IP entries (no domains); DNS resolution is never needed
        var database = BuildDatabase(allowInternet: false, addIpEntry: true, addDomainEntry: false);
        var service = BuildService(database);
        service.Start();

        // Act
        service.ProcessDnsRefresh();

        // Assert
        _firewallService.Verify(f => f.RefreshAllowlistRules(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FirewallAccountSettings>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
    }

    [Fact]
    public void ProcessDnsRefresh_DnsChanged_RefreshCalled()
    {
        // Arrange — domain entry; Start() resolves IPs as "1.1.1.1", refresh resolves to "2.2.2.2"
        var database = BuildDatabase(allowInternet: false, addDomainEntry: true);
        var initialResult = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["example.com"] = ["1.1.1.1"]
        };
        var changedResult = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["example.com"] = ["2.2.2.2"]
        };

        _networkInfo
            .SetupSequence(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(initialResult) // Start() call
            .ReturnsAsync(changedResult); // ProcessDnsRefresh() call

        var service = BuildService(database);
        service.Start();

        // Act
        service.ProcessDnsRefresh();

        // Assert
        _firewallService.Verify(f => f.RefreshAllowlistRules(Sid, Username,
            It.IsAny<FirewallAccountSettings>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Once);
    }

    [Fact]
    public void ProcessDnsRefresh_DnsUnchanged_RefreshNotCalled()
    {
        // Arrange — once the cache is populated, a subsequent refresh with unchanged DNS
        // must not trigger RefreshAllowlistRules. The first call populates the cache
        // regardless of the race with Start's background init.
        var database = BuildDatabase(allowInternet: false, addDomainEntry: true);
        var resolvedResult = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["example.com"] = ["1.1.1.1"]
        };

        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(resolvedResult);

        var service = BuildService(database);
        service.Start();
        service.ProcessDnsRefresh(); // populates cache; may or may not call RefreshAllowlistRules
        _firewallService.Invocations.Clear();

        // Act — second refresh with same DNS result
        service.ProcessDnsRefresh();

        // Assert — no refresh triggered since DNS hasn't changed
        _firewallService.Verify(f => f.RefreshAllowlistRules(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FirewallAccountSettings>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
    }

    [Fact]
    public void ProcessDnsRefresh_AllowInternetTrue_SkipsRefresh()
    {
        // Arrange — domain entry present but AllowInternet=true means neither InitializeCache
        // nor ProcessDnsRefresh will perform DNS resolution or call RefreshAllowlistRules
        var database = BuildDatabase(allowInternet: true, addDomainEntry: true);
        var service = BuildService(database);
        service.Start();

        // Act
        service.ProcessDnsRefresh();

        // Assert
        _firewallService.Verify(f => f.RefreshAllowlistRules(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FirewallAccountSettings>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
    }

    [Fact]
    public void ProcessDnsRefresh_DnsResolutionThrows_DoesNotThrowAndSkipsRefresh()
    {
        // Arrange — DNS always throws; regardless of whether Start's background init or
        // ProcessDnsRefresh runs first, both paths catch the exception and skip the refresh.
        var database = BuildDatabase(allowInternet: false, addDomainEntry: true);
        _networkInfo
            .Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ThrowsAsync(new Exception("DNS timeout"));

        var service = BuildService(database);
        service.Start();

        // Act — must not throw even though DNS resolution fails
        var ex = Record.Exception(() => service.ProcessDnsRefresh());

        // Assert — no exception, RefreshAllowlistRules is never called on failure
        Assert.Null(ex);
        _firewallService.Verify(f => f.RefreshAllowlistRules(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FirewallAccountSettings>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
    }

    [Fact]
    public void ProcessDnsRefresh_LocalhostBlocked_CallsRefreshLocalAddressRules()
    {
        // Arrange — account has AllowLocalhost=false; timer must call RefreshLocalAddressRules each tick.
        var database = BuildDatabase(allowLocalhost: false);
        _firewallService.Setup(f => f.RefreshLocalAddressRules(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FirewallAccountSettings>()))
            .Returns(false);
        var service = BuildService(database);
        service.Start();

        // Act
        service.ProcessDnsRefresh();

        // Assert — RefreshLocalAddressRules called on every tick regardless of changes
        _firewallService.Verify(f => f.RefreshLocalAddressRules(Sid, Username,
            It.IsAny<FirewallAccountSettings>()), Times.Once);
    }

    [Fact]
    public void ProcessDnsRefresh_LocalhostAllowed_DoesNotCallRefreshLocalAddressRules()
    {
        // Arrange — AllowLocalhost=true: no local address block rules exist, nothing to refresh
        var database = BuildDatabase(allowLocalhost: true);
        var service = BuildService(database);
        service.Start();

        // Act
        service.ProcessDnsRefresh();

        // Assert
        _firewallService.Verify(f => f.RefreshLocalAddressRules(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FirewallAccountSettings>()), Times.Never);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var database = BuildDatabase();
        var service = BuildService(database);
        service.Start();

        // Act & Assert
        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var service = BuildService();

        // Act & Assert — dispose without Start, then again
        var ex = Record.Exception(() =>
        {
            service.Dispose();
            service.Dispose();
        });
        Assert.Null(ex);
    }
}