using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundPrivilegeClassifier(
    IProcessPrivilegeStateReader processPrivilegeStateReader,
    IProcessOwnerSidReader processOwnerSidReader,
    IInteractiveUserSidResolver interactiveUserSidResolver,
    ForegroundProcessJobInspector processJobInspector,
    SidDisplayNameResolver sidDisplayNameResolver,
    ILoggingService log)
{
    public ForegroundPrivilegeClassificationResult Classify(ForegroundPrivilegeClassificationRequest request)
    {
        if (request.PrivilegeSubjectProcessId == 0)
        {
            log.Debug($"ForegroundPrivilegeClassifier: hidden request {request.RequestId}; foreground pid is zero.");
            return ForegroundPrivilegeClassificationResult.Hidden(request);
        }

        if (!processPrivilegeStateReader.TryGetProcessElevation(request.PrivilegeSubjectProcessId, out var isElevated))
        {
            log.Debug($"ForegroundPrivilegeClassifier: hidden pid {request.PrivilegeSubjectProcessId}; elevation state is unreadable.");
            return ForegroundPrivilegeClassificationResult.Hidden(request);
        }

        if (isElevated)
        {
            log.Debug($"ForegroundPrivilegeClassifier: hidden pid {request.PrivilegeSubjectProcessId}; process is elevated.");
            return ForegroundPrivilegeClassificationResult.Hidden(
                request,
                isCacheable: true,
                tooltipMode: ForegroundPrivilegeTooltipMode.Elevated);
        }

        if (!processPrivilegeStateReader.TryGetProcessIntegrityLevel(request.PrivilegeSubjectProcessId, out var integrityLevel))
        {
            log.Debug($"ForegroundPrivilegeClassifier: hidden pid {request.PrivilegeSubjectProcessId}; integrity level is unreadable.");
            return ForegroundPrivilegeClassificationResult.Hidden(request);
        }

        if (integrityLevel < NativeTokenHelper.MandatoryLevelMedium)
        {
            log.Debug(
                $"ForegroundPrivilegeClassifier: visible pid {request.PrivilegeSubjectProcessId}; low integrity level 0x{integrityLevel:X}.");
            return ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.LowIL);
        }

        if (integrityLevel > NativeTokenHelper.MandatoryLevelMedium)
        {
            log.Debug(
                $"ForegroundPrivilegeClassifier: hidden pid {request.PrivilegeSubjectProcessId}; high integrity level 0x{integrityLevel:X}.");
            return ForegroundPrivilegeClassificationResult.Hidden(
                request,
                isCacheable: true,
                tooltipMode: ForegroundPrivilegeTooltipMode.HighIL);
        }

        var isolationResult = processJobInspector.TryIsIsolated(request.PrivilegeSubjectProcessId);
        if (isolationResult == ForegroundProcessJobInspectionResult.Isolated)
        {
            log.Debug($"ForegroundPrivilegeClassifier: visible pid {request.PrivilegeSubjectProcessId}; process is in a verified restricted job.");
            return ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Isolated);
        }

        if (isolationResult == ForegroundProcessJobInspectionResult.Unknown)
        {
            log.Debug($"ForegroundPrivilegeClassifier: hidden pid {request.PrivilegeSubjectProcessId}; job isolation state is unknown.");
            return ForegroundPrivilegeClassificationResult.Hidden(request);
        }

        var interactiveUserSid = interactiveUserSidResolver.GetInteractiveUserSid();
        if (string.IsNullOrWhiteSpace(interactiveUserSid))
        {
            log.Debug($"ForegroundPrivilegeClassifier: hidden pid {request.PrivilegeSubjectProcessId}; interactive user SID is unavailable.");
            return ForegroundPrivilegeClassificationResult.Hidden(request, isCacheable: false);
        }

        var processOwnerSid = processOwnerSidReader.TryGetProcessOwnerSid(request.PrivilegeSubjectProcessId);
        if (string.IsNullOrWhiteSpace(processOwnerSid))
        {
            log.Debug($"ForegroundPrivilegeClassifier: hidden pid {request.PrivilegeSubjectProcessId}; process owner SID is unreadable.");
            return ForegroundPrivilegeClassificationResult.Hidden(request, isCacheable: false);
        }

        if (string.Equals(processOwnerSid, interactiveUserSid, StringComparison.OrdinalIgnoreCase))
            return ForegroundPrivilegeClassificationResult.Hidden(request, isCacheable: false);

        log.Debug(
            $"ForegroundPrivilegeClassifier: visible pid {request.PrivilegeSubjectProcessId}; owner {FormatSidForLog(processOwnerSid)} differs from interactive user {FormatSidForLog(interactiveUserSid)}.");
        return ForegroundPrivilegeClassificationResult.Visible(request, ForegroundPrivilegeMarkerKind.Basic, isCacheable: false);
    }

    private string FormatSidForLog(string sid)
    {
        if (!log.Enabled || log.Verbosity < LogVerbosity.Debug)
            return $"SID '{sid}'";

        try
        {
            var displayName = sidDisplayNameResolver.ResolveUsername(sid, null);
            return string.IsNullOrWhiteSpace(displayName) || string.Equals(displayName, sid, StringComparison.OrdinalIgnoreCase)
                ? $"SID '{sid}'"
                : $"'{displayName}' (SID '{sid}')";
        }
        catch
        {
            return $"SID '{sid}'";
        }
    }
}
