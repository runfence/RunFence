using Moq;
using RunFence.Core;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class AuditPolicyServiceTests
{
    private readonly Mock<ILoggingService> _log = new();

    [Fact]
    public void ReadBlockedConnectionAuditingState_UsesGetArguments()
    {
        var reader = new TestAuditPolicyReader(
            _log.Object,
            new AuditPolCommandResult(0, "Failure", string.Empty));

        var result = reader.ReadBlockedConnectionAuditingState();

        Assert.Equal(AuditPolicyStatus.Succeeded, result.Status);
        Assert.Equal("/get /subcategory:{0CCE9226-69AE-11D9-BED3-505054503030}", Assert.Single(reader.Arguments));
    }

    [Fact]
    public void EnableBlockedConnectionAuditing_UsesSetArguments_AndReturnsReadbackMismatch()
    {
        var reader = new TestAuditPolicyReader(
            _log.Object,
            new AuditPolCommandResult(0, "ok", string.Empty),
            new AuditPolCommandResult(0, "No Auditing", string.Empty));

        var result = reader.EnableBlockedConnectionAuditing();

        Assert.Equal(AuditPolicyStatus.ReadbackMismatch, result.Status);
        Assert.Equal(
            [
                "/set /subcategory:{0CCE9226-69AE-11D9-BED3-505054503030} /failure:enable",
                "/get /subcategory:{0CCE9226-69AE-11D9-BED3-505054503030}"
            ],
            reader.Arguments);
    }

    [Fact]
    public void ReadBlockedConnectionAuditingState_NonZeroExit_MapsAccessDenied()
    {
        var reader = new TestAuditPolicyReader(
            _log.Object,
            new AuditPolCommandResult(5, "Failure", "Access is denied."));

        var result = reader.ReadBlockedConnectionAuditingState();

        Assert.Equal(AuditPolicyStatus.AccessDenied, result.Status);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void EnableBlockedConnectionAuditing_StandardError_DoesNotReportSuccess()
    {
        var reader = new TestAuditPolicyReader(
            _log.Object,
            new AuditPolCommandResult(0, "The command completed successfully.", "auditpol error"));

        var result = reader.EnableBlockedConnectionAuditing();

        Assert.Equal(AuditPolicyStatus.Failed, result.Status);
        Assert.Contains("auditpol error", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void DisableBlockedConnectionAuditing_UnsupportedOutput_MapsUnsupported()
    {
        var reader = new TestAuditPolicyReader(
            _log.Object,
            new AuditPolCommandResult(1, string.Empty, "The parameter is invalid."));

        var result = reader.DisableBlockedConnectionAuditing();

        Assert.Equal(AuditPolicyStatus.Unsupported, result.Status);
    }

    [Fact]
    public void ReadBlockedConnectionAuditingState_UnparseableOutput_DoesNotReportSuccess()
    {
        var reader = new TestAuditPolicyReader(
            _log.Object,
            new AuditPolCommandResult(0, "unexpected", string.Empty));

        var result = reader.ReadBlockedConnectionAuditingState();

        Assert.Equal(AuditPolicyStatus.Failed, result.Status);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void ReadBlockedConnectionAuditingState_Timeout_MapsFailedRetryable()
    {
        var reader = new ThrowingAuditPolicyReader(_log.Object, new TimeoutException("auditpol.exe timed out"));

        var result = reader.ReadBlockedConnectionAuditingState();

        Assert.Equal(AuditPolicyStatus.Failed, result.Status);
        Assert.True(result.IsRetryable);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestAuditPolicyReader : IAuditPolCommandRunner
    {
        private readonly EventLogBlockedConnectionReader _reader;
        private readonly Queue<AuditPolCommandResult> _results;

        public TestAuditPolicyReader(ILoggingService log, params AuditPolCommandResult[] results)
        {
            _reader = new EventLogBlockedConnectionReader(log, Mock.Of<IBlockedConnectionEventSource>(), this);
            _results = new Queue<AuditPolCommandResult>(results);
        }

        public List<string> Arguments { get; } = [];

        public AuditPolicyResult ReadBlockedConnectionAuditingState() => _reader.ReadBlockedConnectionAuditingState();

        public AuditPolicyResult EnableBlockedConnectionAuditing() => _reader.EnableBlockedConnectionAuditing();

        public AuditPolicyResult DisableBlockedConnectionAuditing() => _reader.DisableBlockedConnectionAuditing();

        public AuditPolCommandResult Run(string args)
        {
            Arguments.Add(args);
            return _results.Dequeue();
        }
    }

    private sealed class ThrowingAuditPolicyReader : IAuditPolCommandRunner
    {
        private readonly Exception _exception;
        private readonly EventLogBlockedConnectionReader _reader;

        public ThrowingAuditPolicyReader(ILoggingService log, Exception exception)
        {
            _exception = exception;
            _reader = new EventLogBlockedConnectionReader(log, Mock.Of<IBlockedConnectionEventSource>(), this);
        }

        public AuditPolicyResult ReadBlockedConnectionAuditingState() => _reader.ReadBlockedConnectionAuditingState();

        public AuditPolCommandResult Run(string args) => throw _exception;
    }
}
