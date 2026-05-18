using RunFence.Acl;

namespace RunFence.Acl.UI;

public enum DialogApplyPresentationStatus
{
    RenderedSuccess,
    RenderedWarning,
    RenderedValidationFailure,
    RenderedFailure
}

public sealed record DialogApplyPresentationResult(
    DialogApplyPresentationStatus Status,
    int ChangedCount = 0,
    bool RetainPendingInput = false,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<string>? Errors = null);

public class AclDialogApplyPresenter
{
    public DialogApplyPresentationResult Present(
        bool applySucceeded,
        int changedCount,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<string>? errors = null)
    {
        if (applySucceeded)
        {
            return warnings is { Count: > 0 }
                ? new(DialogApplyPresentationStatus.RenderedWarning, changedCount, Warnings: warnings)
                : new(DialogApplyPresentationStatus.RenderedSuccess, changedCount);
        }

        return new(
            DialogApplyPresentationStatus.RenderedFailure,
            RetainPendingInput: true,
            Warnings: warnings,
            Errors: errors);
    }

    public DialogApplyPresentationResult ShowResult(IWin32Window owner, AclApplyOutcome outcome)
    {
        var warnings = outcome.Warnings
            .Select(GrantApplyFailureFormatter.Format)
            .ToList();
        var errors = outcome.Errors
            .SelectMany(FormatErrorLines)
            .ToList();
        var presentation = Present(
            outcome.Succeeded,
            changedCount: outcome.Succeeded ? 1 : 0,
            warnings,
            errors);

        if (outcome.Succeeded)
        {
            if (warnings.Count == 0)
                return presentation;

            var warningMessage =
                $"Changes were applied, but the following operations could not be saved durably:\n\n{string.Join("\n", warnings.Select(warning => $"  {warning}"))}";
            ShowMessage(owner, "Apply Warnings", warningMessage);
            return presentation;
        }

        if (outcome.Errors.Count == 0)
        {
            if (warnings.Count == 0)
                return presentation;

            var warningMessage =
                $"Apply did not complete. Some already-applied operations also reported warnings:\n\nWarnings:\n{string.Join("\n", warnings.Select(warning => $"  {warning}"))}";
            ShowMessage(owner, "Apply Warnings", warningMessage);
            return presentation;
        }

        var sections = new List<string>();
        if (warnings.Count > 0)
            sections.Add($"Warnings:\n{string.Join("\n", warnings.Select(warning => $"  {warning}"))}");
        sections.Add($"Errors:\n{string.Join("\n", errors.Select(error => $"  {error}"))}");

        var message = $"The following operations failed (changes were partially applied):\n\n{string.Join("\n\n", sections)}";
        ShowMessage(owner, "Apply Errors", message);
        return presentation;
    }

    private static IEnumerable<string> FormatErrorLines(GrantOperationException error)
    {
        yield return GrantApplyFailureFormatter.Format(error.Step, error.Path, error.ConfigPath, error.Cause);

        if (error.CleanupFailures.Count == 0)
            yield break;

        yield return "Cleanup failures:";
        foreach (var failure in error.CleanupFailures)
            yield return $"  {GrantApplyFailureFormatter.Format(failure)}";
    }

    protected virtual void ShowMessage(IWin32Window owner, string title, string message)
    {
        MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
