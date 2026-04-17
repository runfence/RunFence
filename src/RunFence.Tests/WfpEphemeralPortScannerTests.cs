using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.Wfp;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class WfpEphemeralPortScannerTests
{
    private const string OwnSid = "S-1-5-21-1234-own";
    private const string OtherSid = "S-1-5-21-1234-other";
    private const string AnotherSid = "S-1-5-21-1234-another";

    private static PortRange P(int port) => new(port, port);
    private static PortRange R(int low, int high) => new(low, high);

    [Fact]
    public void ComputeBlockedEphemeralRanges_OwnedBySameSid_Excluded()
    {
        // Port owned by the same account — never block regardless of exemption
        var portToSid = new Dictionary<int, string?> { { 55000, OwnSid } };
        var exemptions = new List<PortRange> { R(49152, 65535) };
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(OwnSid, exemptions, portToSid);
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_CrossUserInRangeExemption_Included()
    {
        var portToSid = new Dictionary<int, string?> { { 55000, OtherSid } };
        var exemptions = new List<PortRange> { R(49152, 65535) };
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(OwnSid, exemptions, portToSid);
        Assert.Equal([P(55000)], result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_CrossUserNotInAnyExemption_Excluded()
    {
        // Port 55000 is not in any range exemption — static filter already blocks it
        var portToSid = new Dictionary<int, string?> { { 55000, OtherSid } };
        var exemptions = new List<PortRange> { P(53) };
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(OwnSid, exemptions, portToSid);
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_CrossUserInSinglePortExemption_ExcludedEvenIfRangeCoversIt()
    {
        // Both a range exemption and a single-port exemption cover port 55000 — single-port wins
        var portToSid = new Dictionary<int, string?> { { 55000, OtherSid } };
        var exemptions = new List<PortRange> { R(49152, 65535), P(55000) };
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(OwnSid, exemptions, portToSid);
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_CrossUserInRangeButNotSinglePort_Included()
    {
        // Port 55001 is in the range exemption but not in any single-port exemption
        var portToSid = new Dictionary<int, string?> { { 55001, OtherSid } };
        var exemptions = new List<PortRange> { R(49152, 65535), P(55000) };
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(OwnSid, exemptions, portToSid);
        Assert.Equal([P(55001)], result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_UnknownOwner_Included()
    {
        // Null owner (SYSTEM, exited) — treated as cross-user (fail-safe)
        var portToSid = new Dictionary<int, string?> { { 55000, null } };
        var exemptions = new List<PortRange> { R(49152, 65535) };
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(OwnSid, exemptions, portToSid);
        Assert.Equal([P(55000)], result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_DefaultEphemeralRangeExemption_IncludesCrossUserPort()
    {
        // Default scenario: "49152-65535" is a range exemption
        var portToSid = new Dictionary<int, string?> { { 55000, OtherSid } };
        var exemptions = new List<PortRange> { R(49152, 65535) };
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(OwnSid, exemptions, portToSid);
        Assert.Equal([P(55000)], result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_MultiplePorts_CoalescedCorrectly()
    {
        // Three adjacent cross-user ports merged into a single range
        var portToSid = new Dictionary<int, string?>
        {
            { 55000, OtherSid },
            { 55001, OtherSid },
            { 55002, OtherSid }
        };
        var exemptions = new List<PortRange> { R(49152, 65535) };
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(OwnSid, exemptions, portToSid);
        Assert.Equal([R(55000, 55002)], result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_EmptyPortTable_ReturnsEmpty()
    {
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(OwnSid,
            [R(49152, 65535)], []);
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_PortInGapBetweenSplitRanges_Excluded()
    {
        // Port 54500 falls in the 54001-54999 gap — static filter blocks it; scanner must not re-block
        var portToSid = new Dictionary<int, string?> { { 54500, OtherSid } };
        var exemptions = new List<PortRange> { R(52000, 54000), R(55000, 65535) };
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(OwnSid, exemptions, portToSid);
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeBlockedEphemeralRanges_SplitRangeExemptions_IncludesPortsInEachRange()
    {
        // Port 53000 and 60000 are in range exemptions; 54500 is in the gap — excluded
        var portToSid = new Dictionary<int, string?>
        {
            { 53000, OtherSid },
            { 60000, OtherSid },
            { 54500, OtherSid }
        };
        var exemptions = new List<PortRange> { R(52000, 54000), R(55000, 65535) };
        var result = WfpEphemeralPortScanner.ComputeBlockedEphemeralRanges(OwnSid, exemptions, portToSid);
        Assert.Equal([P(53000), P(60000)], result);
    }

    // ── ScanAndApply integration tests ───────────────────────────────────
    // Note: the ScanAndApply tests below are environment-sensitive integration tests.
    // They invoke the real OS TCP/UDP endpoint table APIs to enumerate listening ports and
    // resolve PIDs to SIDs. Port state depends on what is actually listening on the test
    // machine at the time of the run, so assertions involving specific port ranges (other than
    // "expect empty" and "expect called once") may vary across machines and CI environments.

    [Fact]
    public void ScanAndApply_FilterEphemeralFalse_CallsUpdateWithEmptyList()
    {
        // An account with AllowLocalhost=false AND FilterEphemeralLoopback=false
        // falls into the clearAccounts path: UpdateEphemeralPorts must be called with empty list.
        var database = MakeDatabase(new AccountEntry
        {
            Sid = OwnSid,
            Firewall = new FirewallAccountSettings { AllowLocalhost = false, FilterEphemeralLoopback = false }
        });
        var (wfpBlocker, scanner) = CreateScanner(database);

        scanner.Start();

        wfpBlocker.Verify(w => w.UpdateEphemeralPorts(OwnSid, It.Is<IReadOnlyList<PortRange>>(r => r.Count == 0)), Times.Once);
    }

    [Fact]
    public void ScanAndApply_MultiAccount_EligibleAndClearAccountsBothProcessed()
    {
        // Two blocked accounts: one with FilterEphemeral=true (eligible), one with false (clear).
        // Both should receive UpdateEphemeralPorts: eligible gets computed ranges, clear gets empty.
        var database = MakeDatabase(
            new AccountEntry { Sid = OwnSid, Firewall = new FirewallAccountSettings { AllowLocalhost = false, FilterEphemeralLoopback = true } },
            new AccountEntry { Sid = OtherSid, Firewall = new FirewallAccountSettings { AllowLocalhost = false, FilterEphemeralLoopback = false } });
        var (wfpBlocker, scanner) = CreateScanner(database);

        scanner.Start();

        // Eligible account: UpdateEphemeralPorts called (possibly with empty ranges since no actual ports listening in tests)
        wfpBlocker.Verify(w => w.UpdateEphemeralPorts(OwnSid, It.IsAny<IReadOnlyList<PortRange>>()), Times.Once);
        // Clear account: UpdateEphemeralPorts called with empty list
        wfpBlocker.Verify(w => w.UpdateEphemeralPorts(OtherSid, It.Is<IReadOnlyList<PortRange>>(r => r.Count == 0)), Times.Once);
    }

    [Fact]
    public void ScanAndApply_AccountWithAllowLocalhostTrue_NotProcessed()
    {
        // Accounts with AllowLocalhost=true are excluded from both eligible and clearAccounts.
        var database = MakeDatabase(new AccountEntry
        {
            Sid = OwnSid,
            Firewall = new FirewallAccountSettings { AllowLocalhost = true, FilterEphemeralLoopback = false }
        });
        var (wfpBlocker, scanner) = CreateScanner(database);

        scanner.Start();

        wfpBlocker.Verify(w => w.UpdateEphemeralPorts(It.IsAny<string>(), It.IsAny<IReadOnlyList<PortRange>>()), Times.Never);
    }

    [Fact]
    public void ScanAndApply_DefaultFirewallSettings_NotProcessed()
    {
        // An account with IsDefault=true is excluded from both eligible and clearAccounts.
        var database = MakeDatabase(new AccountEntry
        {
            Sid = OwnSid,
            Firewall = new FirewallAccountSettings() // IsDefault=true by default
        });
        var (wfpBlocker, scanner) = CreateScanner(database);

        scanner.Start();

        wfpBlocker.Verify(w => w.UpdateEphemeralPorts(It.IsAny<string>(), It.IsAny<IReadOnlyList<PortRange>>()), Times.Never);
    }

    [Fact]
    public void ScanAndApply_ConsecutiveScans_PidSidCacheEvictionDoesNotCauseErrors()
    {
        // Two consecutive Start() calls simulate two timer ticks. Any PIDs observed during the
        // first scan and absent in the second are evicted from the PID-SID cache. Verify the
        // scanner correctly calls UpdateEphemeralPorts once per scan without throwing.
        var database = MakeDatabase(new AccountEntry
        {
            Sid = OwnSid,
            Firewall = new FirewallAccountSettings { AllowLocalhost = false, FilterEphemeralLoopback = true }
        });
        var (wfpBlocker, scanner) = CreateScanner(database);

        // First scan — PID-SID cache populated for any ports currently listening
        scanner.Start();
        // Second scan — PIDs no longer observed are evicted from cache
        scanner.Start();

        // Two scans → UpdateEphemeralPorts called exactly once per scan
        wfpBlocker.Verify(w => w.UpdateEphemeralPorts(OwnSid, It.IsAny<IReadOnlyList<PortRange>>()), Times.Exactly(2));
    }

    [Fact]
    public void ScanAndApply_MultiAccountWithMixedSettings_EachAccountRoutedCorrectly()
    {
        // Three accounts: eligible (FilterEphemeral=true), clearAccounts (FilterEphemeral=false),
        // and excluded (AllowLocalhost=true). Only the first two receive UpdateEphemeralPorts.
        var database = MakeDatabase(
            new AccountEntry { Sid = OwnSid, Firewall = new FirewallAccountSettings { AllowLocalhost = false, FilterEphemeralLoopback = true } },
            new AccountEntry { Sid = OtherSid, Firewall = new FirewallAccountSettings { AllowLocalhost = false, FilterEphemeralLoopback = false } },
            new AccountEntry { Sid = AnotherSid, Firewall = new FirewallAccountSettings { AllowLocalhost = true, FilterEphemeralLoopback = true } });
        var (wfpBlocker, scanner) = CreateScanner(database);

        scanner.Start();

        // Eligible account gets computed ranges (empty in test environment — no ports listening)
        wfpBlocker.Verify(w => w.UpdateEphemeralPorts(OwnSid, It.IsAny<IReadOnlyList<PortRange>>()), Times.Once);
        // Clear account gets empty list
        wfpBlocker.Verify(w => w.UpdateEphemeralPorts(OtherSid, It.Is<IReadOnlyList<PortRange>>(r => r.Count == 0)), Times.Once);
        // AllowLocalhost=true account is excluded from all ephemeral port processing
        wfpBlocker.Verify(w => w.UpdateEphemeralPorts(AnotherSid, It.IsAny<IReadOnlyList<PortRange>>()), Times.Never);
    }

    [Fact]
    public void ScanAndApply_CreatesDatabaseSnapshotOnUiThread()
    {
        bool insideUiInvoke = false;
        bool providerCalledInsideUiInvoke = false;
        var database = MakeDatabase(new AccountEntry
        {
            Sid = OwnSid,
            Firewall = new FirewallAccountSettings { AllowLocalhost = true }
        });
        var wfpBlocker = new Mock<IWfpLocalhostBlocker>();
        var log = new Mock<ILoggingService>();
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker
            .Setup(u => u.Invoke(It.IsAny<Func<AppDatabase>>()))
            .Returns<Func<AppDatabase>>(f =>
            {
                insideUiInvoke = true;
                try { return f(); }
                finally { insideUiInvoke = false; }
            });
        var dbAccessor = new UiThreadDatabaseAccessor(
            new LambdaDatabaseProvider(() =>
            {
                providerCalledInsideUiInvoke = insideUiInvoke;
                return database;
            }),
            uiThreadInvoker.Object);
        var scanner = new WfpEphemeralPortScanner(
            wfpBlocker.Object,
            dbAccessor,
            log.Object,
            startTimer: false);

        scanner.Start();

        Assert.True(providerCalledInsideUiInvoke);
        wfpBlocker.Verify(w => w.UpdateEphemeralPorts(It.IsAny<string>(), It.IsAny<IReadOnlyList<PortRange>>()), Times.Never);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static AppDatabase MakeDatabase(params AccountEntry[] accounts)
        => new() { Accounts = [..accounts] };

    /// <summary>
    /// Creates a scanner with an <see cref="InlineUiThreadInvoker"/> and a fresh blocker mock.
    /// Use for all <c>ScanAndApply_*</c> tests that do not need a custom <see cref="IUiThreadInvoker"/>.
    /// </summary>
    private static (Mock<IWfpLocalhostBlocker> WfpBlocker, WfpEphemeralPortScanner Scanner) CreateScanner(AppDatabase database)
    {
        var wfpBlocker = new Mock<IWfpLocalhostBlocker>();
        var syncInvoker = new InlineUiThreadInvoker(a => a());
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => database), syncInvoker);
        var scanner = new WfpEphemeralPortScanner(
            wfpBlocker.Object,
            dbAccessor,
            new Mock<ILoggingService>().Object,
            startTimer: false);
        return (wfpBlocker, scanner);
    }
}
