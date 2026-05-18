using RunFence.Core.Models;

namespace RunFence.Firewall;

public class FirewallApplyPlanner
{
    public FirewallApplyPlan BuildApplyPlan(
        string sid,
        FirewallAccountSettings? previousSettings,
        FirewallAccountSettings requestedSettings)
    {
        var previous = previousSettings?.Clone() ?? new FirewallAccountSettings();
        var requested = requestedSettings.Clone();
        var tightenedStage = BuildTightenedStage(previous, requested);
        var phases = new List<FirewallApplyPlanPhase>();

        if (!SettingsEqual(previous, tightenedStage))
        {
            var accountPhase = new FirewallApplyPlanPhase(
                FirewallApplyPlanPhaseKind.AddOrTighten,
                persistConfigBeforeExecution: true,
                shouldPersist: true,
                tightenedStage,
                CreateAccountOperation(sid, previous, tightenedStage),
                globalIcmpOperation: null);
            phases.Add(accountPhase);

            var globalPhase = CreateGlobalPhase(
                FirewallApplyPlanPhaseKind.AddOrTighten,
                persistConfigBeforeExecution: true,
                shouldPersist: false,
                tightenedStage,
                CreateGlobalIcmpOperation(sid, previous, tightenedStage));
            if (globalPhase is not null)
                phases.Add(globalPhase);
        }

        if (!SettingsEqual(tightenedStage, requested))
        {
            var globalOperation = CreateGlobalIcmpOperation(sid, tightenedStage, requested);
            var accountPhase = new FirewallApplyPlanPhase(
                FirewallApplyPlanPhaseKind.RemoveOrLoosen,
                persistConfigBeforeExecution: false,
                shouldPersist: globalOperation is null,
                requested,
                CreateAccountOperation(sid, tightenedStage, requested),
                globalIcmpOperation: null);
            var globalPhase = CreateGlobalPhase(
                FirewallApplyPlanPhaseKind.RemoveOrLoosen,
                persistConfigBeforeExecution: false,
                shouldPersist: true,
                requested,
                globalOperation);

            phases.Add(accountPhase);
            if (globalPhase is not null)
                phases.Add(globalPhase);
        }

        return new FirewallApplyPlan(
            phases,
            requiresNoOpSuccessEntries: phases.Count == 0,
            updatesAccountRetryState: phases.Any(phase => phase.AccountOperation is not null),
            updatesGlobalIcmpRetryState: phases.Any(phase => phase.GlobalIcmpOperation is not null));
    }

    private static FirewallOperation CreateAccountOperation(
        string sid,
        FirewallAccountSettings previous,
        FirewallAccountSettings requested)
        => new(FirewallEnforcementLayer.AccountRules, sid, previous.Clone(), requested.Clone());

    private static FirewallOperation? CreateGlobalIcmpOperation(
        string sid,
        FirewallAccountSettings previous,
        FirewallAccountSettings requested)
        => AffectsGlobalIcmp(previous, requested)
            ? new FirewallOperation(FirewallEnforcementLayer.GlobalIcmp, sid, previous.Clone(), requested.Clone())
            : null;

    private static FirewallApplyPlanPhase? CreateGlobalPhase(
        FirewallApplyPlanPhaseKind kind,
        bool persistConfigBeforeExecution,
        bool shouldPersist,
        FirewallAccountSettings targetSettings,
        FirewallOperation? globalOperation)
        => globalOperation is null
            ? null
            : new FirewallApplyPlanPhase(
                kind,
                persistConfigBeforeExecution,
                shouldPersist,
                targetSettings.Clone(),
                accountOperation: null,
                globalOperation);

    private static FirewallAccountSettings BuildTightenedStage(FirewallAccountSettings previous, FirewallAccountSettings requested)
    {
        var previousAllowlist = NormalizeAllowlist(previous.Allowlist);
        var requestedAllowlist = NormalizeAllowlist(requested.Allowlist);
        var previousPorts = NormalizeStrings(previous.LocalhostPortExemptions);
        var requestedPorts = NormalizeStrings(requested.LocalhostPortExemptions);

        return new FirewallAccountSettings
        {
            AllowInternet = requested.AllowInternet ? previous.AllowInternet : false,
            AllowLocalhost = requested.AllowLocalhost ? previous.AllowLocalhost : false,
            AllowLan = requested.AllowLan ? previous.AllowLan : false,
            Allowlist = previous.Allowlist
                .Where(entry => previousAllowlist.Contains(NormalizeAllowlistEntry(entry))
                    && requestedAllowlist.Contains(NormalizeAllowlistEntry(entry)))
                .Select(CloneAllowlistEntry)
                .ToList(),
            LocalhostPortExemptions = previous.LocalhostPortExemptions
                .Where(entry => previousPorts.Contains(NormalizeString(entry))
                    && requestedPorts.Contains(NormalizeString(entry)))
                .ToList(),
            FilterEphemeralLoopback = requested.FilterEphemeralLoopback || previous.FilterEphemeralLoopback
        };
    }

    private static bool AffectsGlobalIcmp(FirewallAccountSettings previous, FirewallAccountSettings requested)
        => previous.AllowInternet != requested.AllowInternet
           || !NormalizeAllowlist(previous.Allowlist).SetEquals(NormalizeAllowlist(requested.Allowlist));

    private static bool SettingsEqual(FirewallAccountSettings left, FirewallAccountSettings right)
        => left.AllowInternet == right.AllowInternet
           && left.AllowLocalhost == right.AllowLocalhost
           && left.AllowLan == right.AllowLan
           && left.FilterEphemeralLoopback == right.FilterEphemeralLoopback
           && NormalizeAllowlist(left.Allowlist).SetEquals(NormalizeAllowlist(right.Allowlist))
           && NormalizeStrings(left.LocalhostPortExemptions).SetEquals(NormalizeStrings(right.LocalhostPortExemptions));

    private static HashSet<string> NormalizeAllowlist(IEnumerable<FirewallAllowlistEntry> entries)
        => entries
            .Select(NormalizeAllowlistEntry)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeAllowlistEntry(FirewallAllowlistEntry entry)
        => $"{entry.IsDomain}:{NormalizeString(entry.Value)}";

    private static HashSet<string> NormalizeStrings(IEnumerable<string> values)
        => values
            .Select(NormalizeString)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeString(string value)
        => value.Trim();

    private static FirewallAllowlistEntry CloneAllowlistEntry(FirewallAllowlistEntry entry)
        => new()
        {
            Value = entry.Value,
            IsDomain = entry.IsDomain
        };
}
