using RunFence.Core;
using RunFence.Core.Models;
using RunFence.UI.Forms;

namespace RunFence.UI;

public interface ISidEntryHelper
{
    string? ResolveOrPrompt(string accountName, List<LocalUserAccount>? localUsers, IWin32Window? owner);
}

/// <summary>
/// Shared UI helper for resolving account names to SIDs with manual fallback.
/// </summary>
public class SidEntryHelper(ISidResolver sidResolver) : ISidEntryHelper
{
    /// <summary>
    /// Resolves an account name to a SID using SidResolutionHelper, local users list,
    /// and a manual SID entry dialog as last resort. Returns null if the user cancels.
    /// </summary>
    public string? ResolveOrPrompt(string accountName, List<LocalUserAccount>? localUsers, IWin32Window? owner)
    {
        var sid = sidResolver.ResolveSidFromName(accountName, localUsers);
        if (sid != null)
            return sid;

        using var dlg = new ManualSidEntryDialog(accountName);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.ResultSid : null;
    }
}