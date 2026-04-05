using RunFence.Core.Models;

namespace RunFence.Account.UI.AppContainer;

/// <summary>
/// Represents an AppContainer entry in account combo boxes (AppEditDialog, RunAsDialog).
/// Analogous to CredentialDisplayItem for user accounts.
/// </summary>
public class AppContainerDisplayItem(AppContainerEntry container, string containerSid)
{
    public AppContainerEntry Container { get; } = container;

    /// <summary>Pre-computed container SID (set at item creation to avoid repeated P/Invoke).</summary>
    public string ContainerSid { get; } = containerSid;

    public override string ToString() => $"{Container.DisplayName} (container)";
}