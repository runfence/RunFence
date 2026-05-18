using System.Text;
using RunFence.Core.Models;

namespace RunFence.SidMigration.UI;

public class SidMigrationDiscoveryStepController
{
    public SidMigrationDiscoveryStepResult BuildResult(IReadOnlyList<OrphanedSid> orphanedSids)
    {
        var confirmedCount = orphanedSids.Count(s => s.Classification == OrphanedSidClassification.ConfirmedOrphaned);
        var unresolvedSids = orphanedSids
            .Where(s => s.Classification == OrphanedSidClassification.Unresolved)
            .Select(s => s.Sid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unresolvedCount = unresolvedSids.Count;
        var completionText = $"Discovery finished.{Environment.NewLine}{Environment.NewLine}" +
            $"Confirmed orphaned SIDs: {confirmedCount}{Environment.NewLine}" +
            $"Unresolved lookups: {unresolvedCount}";

        var cancelText = orphanedSids.Count > 0
            ? $"Scan cancelled. Confirmed: {confirmedCount}, unresolved: {unresolvedCount}."
            : "Scan cancelled.";

        if (unresolvedCount == 0)
            return new SidMigrationDiscoveryStepResult(completionText, cancelText, null);

        var warning = new StringBuilder();
        warning.AppendLine(completionText);
        warning.AppendLine();
        warning.AppendLine("Unresolved:");
        warning.AppendLine(string.Join(Environment.NewLine, unresolvedSids));
        warning.AppendLine();
        warning.AppendLine("Unresolved SIDs stay skipped unless you explicitly include them in the next step.");

        return new SidMigrationDiscoveryStepResult(completionText, cancelText, warning.ToString().TrimEnd());
    }
}
