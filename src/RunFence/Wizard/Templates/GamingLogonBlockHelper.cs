using RunFence.Account;

namespace RunFence.Wizard.Templates;

public interface IGamingLogonBlockHelper
{
    void CheckAndPromptLogonUnblock(string sid, string username, IWin32Window? owner, IWizardProgressReporter progress);
}

/// <summary>
/// Checks whether a gaming account's logon is blocked and prompts the user to enable it
/// if needed. Gaming account setup requires interactive logon (Win+L) to install games.
/// </summary>
public class GamingLogonBlockHelper(
    IAccountLoginRestrictionService accountRestriction,
    IAccountToggleService accountToggle)
    : IGamingLogonBlockHelper
{
    /// <summary>
    /// Checks if the account's logon is blocked and, if so, prompts the user to enable it.
    /// If the user confirms and unblocking fails, reports an error via <paramref name="progress"/>
    /// but does not stop the wizard — the caller continues regardless of success.
    /// </summary>
    /// <param name="sid">The account SID to check.</param>
    /// <param name="username">The resolved display username for prompt text.</param>
    /// <param name="owner">Owner window for the prompt dialog, or null for no owner.</param>
    /// <param name="progress">Progress reporter for status and error messages.</param>
    public void CheckAndPromptLogonUnblock(string sid, string username, IWin32Window? owner, IWizardProgressReporter progress)
    {
        if (!accountRestriction.IsLoginBlockedBySid(sid))
            return;

        var answer = MessageBox.Show(
            owner,
            $"The account '{username}' has logon blocked.\n\n" +
            "The gaming account setup instructions require logging in interactively (Win+L). " +
            "Do you want to enable logon for this account?",
            "Enable Logon?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        progress.ReportStatus($"Enabling logon for '{username}'...");
        var result = accountToggle.SetLogonBlocked(sid, username, blocked: false);
        if (!result.Success)
            progress.ReportError($"Enable logon: {result.ErrorMessage}");
    }
}
