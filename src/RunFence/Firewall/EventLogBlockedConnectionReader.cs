using RunFence.Core;

namespace RunFence.Firewall;

public class EventLogBlockedConnectionReader(
    ILoggingService log,
    IBlockedConnectionEventSource eventSource,
    IAuditPolCommandRunner auditPolCommandRunner)
    : IBlockedConnectionReader, IAuditPolicyService
{
    private const string AuditSubcategoryGuid = "{0CCE9226-69AE-11D9-BED3-505054503030}";

    public List<BlockedConnection> ReadBlockedConnections(TimeSpan lookback)
    {
        var result = new List<BlockedConnection>();
        var since = DateTime.UtcNow - lookback;

        try
        {
            foreach (var record in eventSource.ReadBlockedConnectionEvents(since))
            {
                result.Add(new BlockedConnection(record.DestAddress, record.DestPort, record.TimeCreatedUtc));
            }
        }
        catch (Exception ex)
        {
            log.Error("BlockedConnectionReader: failed to read Security event log", ex);
        }

        return result;
    }

    public AuditPolicyResult ReadBlockedConnectionAuditingState()
    {
        try
        {
            var commandResult = auditPolCommandRunner.Run($"/get /subcategory:{AuditSubcategoryGuid}");
            if (!TryValidateAuditPolResult(commandResult, requestedState: false, out var failureResult))
                return failureResult!;

            if (!TryParseAuditState(commandResult.StandardOutput, out var enabled))
                return new AuditPolicyResult(AuditPolicyStatus.Failed, false, null, "Failed to parse auditpol output.", IsRetryable: true);

            return new AuditPolicyResult(AuditPolicyStatus.Succeeded, enabled, enabled, null, IsRetryable: false);
        }
        catch (Exception ex)
        {
            log.Error("BlockedConnectionReader: failed to query audit policy", ex);
            return new AuditPolicyResult(AuditPolicyStatus.Failed, false, null, ex.Message, IsRetryable: true);
        }
    }

    public AuditPolicyResult EnableBlockedConnectionAuditing()
    {
        return SetAuditPolicyEnabledCore(true);
    }

    public AuditPolicyResult DisableBlockedConnectionAuditing()
    {
        return SetAuditPolicyEnabledCore(false);
    }

    private AuditPolicyResult SetAuditPolicyEnabledCore(bool enabled)
    {
        var failure = enabled ? "enable" : "disable";
        try
        {
            var commandResult = auditPolCommandRunner.Run($"/set /subcategory:{AuditSubcategoryGuid} /failure:{failure}");
            if (!TryValidateAuditPolResult(commandResult, enabled, out var failureResult))
                return failureResult!;

            var readback = ReadBlockedConnectionAuditingState();
            if (readback.Status == AuditPolicyStatus.Succeeded && readback.ObservedState != enabled)
                return new AuditPolicyResult(AuditPolicyStatus.ReadbackMismatch, enabled, readback.ObservedState, null, IsRetryable: true);

            if (readback.Status != AuditPolicyStatus.Succeeded)
                return new AuditPolicyResult(AuditPolicyStatus.Failed, enabled, readback.ObservedState, readback.Error, IsRetryable: true);

            return new AuditPolicyResult(AuditPolicyStatus.Succeeded, enabled, readback.ObservedState, null, IsRetryable: false);
        }
        catch (Exception ex)
        {
            log.Error($"BlockedConnectionReader: failed to set audit policy (enabled={enabled})", ex);
            return new AuditPolicyResult(AuditPolicyStatus.Failed, enabled, null, ex.Message, IsRetryable: true);
        }
    }

    private static bool TryParseAuditState(string output, out bool enabled)
    {
        if (output.Contains("Failure", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        if (output.Contains("No Auditing", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
            return true;
        }

        enabled = false;
        return false;
    }

    private static bool TryValidateAuditPolResult(
        AuditPolCommandResult commandResult,
        bool requestedState,
        out AuditPolicyResult? failureResult)
    {
        failureResult = null;
        var combinedError = string.Join(
            Environment.NewLine,
            new[] { commandResult.StandardError, commandResult.StandardOutput }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        var normalized = combinedError.Trim();

        if (commandResult.ExitCode == 0 && string.IsNullOrWhiteSpace(commandResult.StandardError))
            return true;

        var status = normalized.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
            ? AuditPolicyStatus.AccessDenied
            : normalized.Contains("not supported", StringComparison.OrdinalIgnoreCase)
              || normalized.Contains("unknown", StringComparison.OrdinalIgnoreCase)
              || normalized.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                ? AuditPolicyStatus.Unsupported
                : AuditPolicyStatus.Failed;
        var retryable = status == AuditPolicyStatus.Failed;
        failureResult = new AuditPolicyResult(
            status,
            requestedState,
            null,
            string.IsNullOrWhiteSpace(normalized)
                ? $"auditpol.exe exited with code {commandResult.ExitCode}."
                : normalized,
            retryable);
        return false;
    }
}
