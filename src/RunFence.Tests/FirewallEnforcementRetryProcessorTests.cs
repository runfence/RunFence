using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallEnforcementRetryProcessorTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string Username = "alice";

    private readonly Mock<IFirewallAccountRuleApplier> _accountRuleApplier = new();
    private readonly Mock<IAuditPolicyService> _auditPolicy = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly FirewallResolvedDomainCache _domainCache = new(new FirewallDomainDirtyTracker());
    private readonly FirewallEnforcementRetryState _retryState = new();

    public FirewallEnforcementRetryProcessorTests()
    {
        _auditPolicy.Setup(a => a.EnableBlockedConnectionAuditing())
            .Returns(new AuditPolicyResult(AuditPolicyStatus.Succeeded, true, true, null, IsRetryable: false));
        _auditPolicy.Setup(a => a.DisableBlockedConnectionAuditing())
            .Returns(new AuditPolicyResult(AuditPolicyStatus.Succeeded, false, false, null, IsRetryable: false));
    }

    [Fact]
    public void ProcessRetries_AuditRetry_SucceedsAndClearsEntry()
    {
        var database = BuildDatabase();
        _retryState.MarkRetryPending(
            FirewallEnforcementLayer.AuditPolicy,
            "enabled",
            "audit failed",
            "retry audit");
        var processor = BuildProcessor();

        processor.ProcessRetries(database);

        Assert.DoesNotContain(_retryState.GetRetryEntries(), entry => entry.Layer == FirewallEnforcementLayer.AuditPolicy);
        _auditPolicy.Verify(a => a.EnableBlockedConnectionAuditing(), Times.Once);
    }

    [Fact]
    public void ProcessRetries_AccountAndWfpForSameSid_RetriesOnceAndClearsBoth()
    {
        var database = BuildDatabase();
        _retryState.MarkRetryPending(
            FirewallEnforcementLayer.AccountRules,
            Sid,
            "account failed",
            "retry account");
        _retryState.MarkRetryPending(
            FirewallEnforcementLayer.WfpFilters,
            Sid,
            "wfp failed",
            "retry wfp");
        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Returns(new FirewallAccountRuleApplyResult(true, []));
        var processor = BuildProcessor();

        processor.ProcessRetries(database);

        _accountRuleApplier.Verify(a => a.ApplyFirewallRules(
            Sid,
            Username,
            It.IsAny<FirewallAccountSettings>(),
            It.IsAny<FirewallAccountSettings?>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Once);
        Assert.DoesNotContain(_retryState.GetRetryEntries(), entry =>
            entry.Key == Sid &&
            (entry.Layer == FirewallEnforcementLayer.AccountRules || entry.Layer == FirewallEnforcementLayer.WfpFilters));
    }

    [Fact]
    public void ProcessRetries_MissingAccount_ClearsAccountAndWfpEntries()
    {
        var database = new AppDatabase();
        _retryState.MarkRetryPending(
            FirewallEnforcementLayer.AccountRules,
            Sid,
            "account failed",
            "retry account");
        _retryState.MarkRetryPending(
            FirewallEnforcementLayer.WfpFilters,
            Sid,
            "wfp failed",
            "retry wfp");
        var processor = BuildProcessor();

        processor.ProcessRetries(database);

        Assert.DoesNotContain(_retryState.GetRetryEntries(), entry =>
            entry.Key == Sid &&
            (entry.Layer == FirewallEnforcementLayer.AccountRules || entry.Layer == FirewallEnforcementLayer.WfpFilters));
        _accountRuleApplier.Verify(a => a.ApplyFirewallRules(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<FirewallAccountSettings>(),
            It.IsAny<FirewallAccountSettings?>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()), Times.Never);
    }

    [Fact]
    public void ProcessRetries_WhenRetryThrows_RequeuesEntryAndLogs()
    {
        var database = BuildDatabase();
        _retryState.MarkRetryPending(
            FirewallEnforcementLayer.AccountRules,
            Sid,
            "account failed",
            "retry account");
        _accountRuleApplier
            .Setup(a => a.ApplyFirewallRules(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>()))
            .Throws(new InvalidOperationException("retry failed"));
        var processor = BuildProcessor();

        processor.ProcessRetries(database);

        Assert.Contains(_retryState.GetRetryEntries(), entry =>
            entry.Layer == FirewallEnforcementLayer.AccountRules && entry.Key == Sid);
        _log.Verify(l => l.Error(
            It.Is<string>(message => message.Contains("Failed to retry firewall enforcement layer", StringComparison.Ordinal)),
            It.IsAny<Exception>()), Times.Once);
    }

    private FirewallEnforcementRetryProcessor BuildProcessor()
        => new(
            _accountRuleApplier.Object,
            _auditPolicy.Object,
            _domainCache,
            _retryState,
            _log.Object);

    private static AppDatabase BuildDatabase()
    {
        var database = new AppDatabase
        {
            SidNames =
            {
                [Sid] = Username
            }
        };
        var account = database.GetOrCreateAccount(Sid);
        account.Firewall = new FirewallAccountSettings { AllowInternet = false };
        return database;
    }
}
