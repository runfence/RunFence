namespace RunFence.Firewall.UI;

using RunFence.Acl.UI;

public sealed class FirewallDialogApplyPresenter
{
    public DialogApplyPresentationResult Present(bool rolledBack, int changedSettingsCount, IReadOnlyList<string>? warnings = null)
    {
        if (rolledBack)
            return new DialogApplyPresentationResult(
                DialogApplyPresentationStatus.RenderedWarning,
                ChangedCount: changedSettingsCount,
                RetainPendingInput: true,
                Warnings: warnings ?? ["Changes were rolled back."]);

        return new DialogApplyPresentationResult(
            DialogApplyPresentationStatus.RenderedSuccess,
            ChangedCount: changedSettingsCount,
            Warnings: warnings);
    }
}
