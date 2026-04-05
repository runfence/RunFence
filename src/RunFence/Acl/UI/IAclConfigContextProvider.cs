namespace RunFence.Acl.UI;

/// <summary>
/// Provides context values to <see cref="Forms.AclConfigSection"/> from its hosting dialog.
/// Replaces the previous delegate-per-callback pattern in <see cref="Forms.AclConfigContext"/>.
/// </summary>
public interface IAclConfigContextProvider
{
    string GetExePath();
    string? GetSelectedAccountSid();
    bool IsContainerSelected();
    void OnSidNameLearned(string sid, string name);
}