using RunFence.Apps.UI;
using RunFence.Core.Models;

namespace RunFence.SidMigration.UI;

public class SidMigrationSelectionCollector(
    SidMigrationMappingValidator validator,
    IMessageBoxService messageBoxService,
    IReadOnlyDictionary<string, string> sidDisplayNames,
    IReadOnlyDictionary<string, OrphanedSid> orphanedBySid)
{
    public SidMigrationSelectionResult Collect(
        IReadOnlyList<SidMigrationSelectionRow> rows,
        Func<IEnumerable<string>, bool> confirmUnresolvedSelections)
    {
        var result = new List<SidMigrationMapping>();
        var deleteSids = new List<string>();
        var rowErrors = new Dictionary<int, string>();

        var rowsByNewSid = new Dictionary<string, List<SidMigrationSelectionRow>>(StringComparer.OrdinalIgnoreCase);
        var validationErrors = new List<string>();

        foreach (var row in rows)
        {
            if (!string.Equals(row.Action, "Migrate", StringComparison.Ordinal))
                continue;
            if (string.IsNullOrWhiteSpace(row.OldSid) || string.IsNullOrWhiteSpace(row.NewSidInput))
                continue;

            if (row.Name == "(manual)" && !SidMigrationMappingValidator.TryParseSid(row.OldSid, out _))
            {
                AddRowError(rowErrors, row.RowIndex, $"'{row.OldSid}' is not a valid SID.");
                validationErrors.Add($"'{row.OldSid}' is not a valid SID.");
                continue;
            }

            if (!SidMigrationMappingValidator.TryResolveSidInput(row.NewSidInput, sidDisplayNames, out var newSid))
            {
                AddRowError(rowErrors, row.RowIndex, $"'{row.NewSidInput}' is not a valid SID.");
                validationErrors.Add($"'{row.NewSidInput}' is not a valid SID.");
                continue;
            }

            if (!rowsByNewSid.TryGetValue(newSid, out var rowList))
            {
                rowList = [];
                rowsByNewSid[newSid] = rowList;
            }
            rowList.Add(row);

            result.Add(new SidMigrationMapping(row.OldSid, newSid, row.Name));
        }

        var duplicateNewSids = validator.FindDuplicateNewSids(result);
        if (duplicateNewSids.Count > 0)
        {
            foreach (var (newSid, duplicateRows) in rowsByNewSid)
            {
                if (!duplicateNewSids.Contains(newSid))
                    continue;

                foreach (var row in duplicateRows)
                {
                    AddRowError(rowErrors, row.RowIndex, "Duplicate target SID — each old SID must map to a unique new SID.");
                    validationErrors.Add($"Duplicate target SID: {newSid}");
                }
            }
        }

        if (validationErrors.Count > 0)
        {
            messageBoxService.Show(
                "Please fix the following validation errors before proceeding:\n\n" +
                string.Join("\n", validationErrors.Distinct(StringComparer.Ordinal)),
                "Validation Errors",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return new SidMigrationSelectionResult(false, [], [], rowErrors);
        }

        deleteSids = rows
            .Where(row => string.Equals(row.Action, "Delete", StringComparison.Ordinal)
                          && !string.IsNullOrWhiteSpace(row.OldSid))
            .Select(row => row.OldSid.Trim())
            .ToList();

        if (!confirmUnresolvedSelections(result.Select(m => m.OldSid).Concat(deleteSids)))
            return new SidMigrationSelectionResult(false, [], [], rowErrors);

        var deleteBlockedByOwnerRefs = deleteSids
            .Where(sid => orphanedBySid.TryGetValue(sid, out var orphan) && orphan.OwnerCount > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (deleteBlockedByOwnerRefs.Count > 0)
        {
            messageBoxService.Show(
                "Delete cannot be used for SIDs that still own files or folders.\n\n" +
                string.Join("\n", deleteBlockedByOwnerRefs.Select(sid => validator.ResolveSidName(sid) ?? sid)) +
                "\n\nChoose Migrate and provide a replacement owner SID instead.",
                "Owner Migration Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return new SidMigrationSelectionResult(false, [], [], rowErrors);
        }

        return new SidMigrationSelectionResult(true, result, deleteSids, rowErrors);
    }

    private static void AddRowError(
        IDictionary<int, string> rowErrors,
        int rowIndex,
        string errorText)
    {
        if (!rowErrors.ContainsKey(rowIndex))
            rowErrors[rowIndex] = errorText;
    }
}
