namespace RunFence.Acl.UI;

/// <summary>
/// Provides grid refresh callbacks from <see cref="RunFence.Acl.UI.Forms.AclManagerDialog"/> to
/// <see cref="AclManagerActionOrchestrator"/>, ensuring sort glyphs are preserved after repopulation.
/// </summary>
public interface IAclManagerGridRefresher
{
    void RefreshGrantsGrid();
    void RefreshTraverseGrid();
}
