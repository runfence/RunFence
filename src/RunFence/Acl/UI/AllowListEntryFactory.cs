using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs.UI.Forms;
using RunFence.UI;

namespace RunFence.Acl.UI;

/// <summary>
/// Factory for creating and pre-populating ACL allow-list entries.
/// Extracts SID resolution, group lookup, and entry construction from <see cref="Forms.AclConfigSection"/>.
/// </summary>
public class AllowListEntryFactory(
    ILocalUserProvider localUserProvider,
    ILocalGroupMembershipService groupMembership,
    ISidEntryHelper sidEntryHelper,
    SidDisplayNameResolver displayNameResolver)
{
    /// <summary>
    /// Resolves the display name for a SID using the standard fallback chain.
    /// </summary>
    public string GetDisplayName(string sid, string? preResolvedName,
        IReadOnlyDictionary<string, string>? sidNames)
        => displayNameResolver.GetDisplayName(sid, preResolvedName, sidNames);

    /// <summary>
    /// Shows the identity picker dialog and constructs a new allow-list entry.
    /// Returns null if the user cancels. Returns a result with <see cref="PromptNewEntryResult.IsDuplicate"/>
    /// set to true (and no entry) if the selected SID is already in <paramref name="existingEntries"/>.
    /// </summary>
    public async Task<PromptNewEntryResult?> PromptNewEntryAsync(
        string? selectedAccountSid,
        IReadOnlyDictionary<string, string>? sidNames,
        IWin32Window? owner,
        IReadOnlyList<AllowAclEntry> existingEntries)
    {
        var localUsers = localUserProvider.GetLocalUserAccounts();

        if (!string.IsNullOrEmpty(selectedAccountSid))
        {
            var groups = await Task.Run(() => groupMembership.GetGroupsForUser(selectedAccountSid));
            if (groups.Count > 0)
                localUsers = groups.Concat(localUsers).ToList();
        }

        using var dlg = new CallerIdentityDialog(localUsers, sidEntryHelper);
        if (dlg.ShowDialog(owner) != DialogResult.OK || dlg.Result == null)
            return null;

        foreach (var existing in existingEntries)
        {
            if (string.Equals(existing.Sid, dlg.Result, StringComparison.OrdinalIgnoreCase))
                return new PromptNewEntryResult(null, null, null, IsDuplicate: true);
        }

        var entry = new AllowAclEntry { Sid = dlg.Result, AllowExecute = true, AllowWrite = false };
        var displayName = displayNameResolver.GetDisplayName(entry.Sid, null, sidNames);
        return new PromptNewEntryResult(entry, dlg.ResolvedName, displayName, IsDuplicate: false);
    }

    /// <summary>
    /// Builds the initial allow-list entries to pre-populate when switching to Allow mode.
    /// Always includes an entry for <paramref name="sid"/>. For containers, also includes the
    /// interactive user SID if it differs from the container SID.
    /// Returns a list of <see cref="PrePopulatedEntry"/> with entry and display name.
    /// </summary>
    public List<PrePopulatedEntry> BuildPrePopulationEntries(
        string sid,
        bool isContainer,
        IReadOnlyDictionary<string, string>? sidNames)
    {
        var result = new List<PrePopulatedEntry>();

        // Write access is off by default — app directories rarely need user write access
        // and granting write poses a security risk.
        var entry = new AllowAclEntry { Sid = sid, AllowExecute = true, AllowWrite = false };
        result.Add(new PrePopulatedEntry(entry, displayNameResolver.GetDisplayName(sid, null, sidNames)));

        // Container apps need both the container package SID and the interactive user SID.
        // The container SID was added above (via GetSelectedAccountSid returning the container SID).
        // Now add the interactive user SID so the desktop user token can also reach the exe
        // (AppContainer dual access check: user SID must pass step 1 independently).
        if (isContainer)
        {
            var interactiveSid = NativeTokenHelper.TryGetInteractiveUserSid()?.Value;
            if (!string.IsNullOrEmpty(interactiveSid) &&
                !string.Equals(interactiveSid, sid, StringComparison.OrdinalIgnoreCase))
            {
                var iEntry = new AllowAclEntry { Sid = interactiveSid, AllowExecute = true, AllowWrite = false };
                result.Add(new PrePopulatedEntry(
                    iEntry,
                    displayNameResolver.GetDisplayName(interactiveSid, null, sidNames)));
            }
        }

        return result;
    }
}

/// <summary>
/// Result of <see cref="AllowListEntryFactory.PromptNewEntryAsync"/>. A null return means the user cancelled.
/// When <see cref="IsDuplicate"/> is true, <see cref="Entry"/> is null — the selected SID is already in the list.
/// </summary>
public record PromptNewEntryResult(
    AllowAclEntry? Entry,
    string? ResolvedName,
    string? DisplayName,
    bool IsDuplicate);

/// <summary>
/// An entry with its pre-resolved display name, returned by
/// <see cref="AllowListEntryFactory.BuildPrePopulationEntries"/>.
/// </summary>
public record PrePopulatedEntry(AllowAclEntry Entry, string DisplayName);
