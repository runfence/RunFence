using RunFence.Wizard.UI.Forms;

namespace RunFence.Wizard.UI;

/// <summary>
/// Opens the wizard dialog on behalf of toolbar buttons in AccountsPanel and ApplicationsPanel.
/// Fires <see cref="WizardCompleted"/> after the wizard closes and all post-wizard actions have
/// executed, but only if at least one template was completed during the session.
/// Post-wizard actions queued by individual templates (e.g., opening the firewall allowlist dialog
/// for AI Agent) are executed sequentially after the dialog closes, before <see cref="WizardCompleted"/>.
/// </summary>
public class WizardLauncher(Func<WizardDialog> dialogFactory, IEnumerable<IWizardTemplate> templates)
{
    /// <summary>
    /// Fired after the wizard dialog closes and all post-wizard actions have executed,
    /// only if at least one template was completed. Subscribe to trigger <c>DataChanged</c>
    /// or any other UI refresh not already handled by the completed templates.
    /// </summary>
    public event Action? WizardCompleted;

    /// <summary>
    /// Opens the wizard dialog modally. Concurrently invokes each template's
    /// <see cref="IWizardTemplate.WarmCacheAsync"/> before constructing the dialog so that
    /// availability checks never block the UI thread.
    /// Executes any post-wizard actions queued by completed templates, then fires <see cref="WizardCompleted"/>.
    /// </summary>
    public async Task OpenWizardAsync(IWin32Window owner)
    {
        await Task.WhenAll(templates.Select(t => t.WarmCacheAsync()));

        using var dlg = dialogFactory();
        dlg.ShowDialog(owner);

        foreach (var action in dlg.PostWizardActions)
            action(owner);

        // Only notify if at least one template was completed; avoids spurious save/refresh on Cancel.
        if (dlg.TemplateCompletedCount > 0)
            WizardCompleted?.Invoke();
    }
}