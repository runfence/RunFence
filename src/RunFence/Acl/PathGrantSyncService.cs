using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Acl.Traverse;

namespace RunFence.Acl;

/// <summary>
/// Synchronizes grant DB entries with NTFS state. Reads ACEs from the NTFS ACL for a given
/// path and creates or updates matching <see cref="GrantedPathEntry"/> records in tracked
/// runtime/config intent state.
/// </summary>
public class PathGrantSyncService(
    UiThreadDatabaseAccessor dbAccessor,
    IGrantAceService grantAceService,
    Func<IGrantIntentStoreProvider> grantIntentStoreProvider,
    Func<IGrantIntentRepository> grantIntentRepository,
    ILoggingService log,
    IFileSystemPathInfo pathInfo,
    ITraverseGrantOwnerResolver ownerResolver) : IGrantSyncService
{
    private IGrantIntentStoreProvider GrantIntentStoreProvider => grantIntentStoreProvider();

    private IGrantIntentRepository GrantIntentRepository => grantIntentRepository();

    /// <summary>
    /// Reads actual NTFS ACEs for <paramref name="path"/> for a specific <paramref name="sid"/>
    /// (or all local-user SIDs if null). For each discovered ACE, creates or updates a matching
    /// <see cref="GrantedPathEntry"/> in tracked runtime/config intent state. Traverse-only ACEs produce
    /// <see cref="GrantedPathEntry.IsTraverseOnly"/> entries. Returns true if any DB entry was
    /// added or updated.
    /// </summary>
    public bool UpdateFromPath(string path, string? sid = null)
    {
        var normalized = Path.GetFullPath(path);
        bool isFolder = pathInfo.DirectoryExists(normalized);
        if (!isFolder && !pathInfo.FileExists(normalized))
            return false;

        try
        {
            var security = grantAceService.GetSecurity(normalized);
            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false,
                typeof(SecurityIdentifier));
            var ownerIdentity = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            bool isAdminOwner = ownerIdentity != null && ownerIdentity.Equals(adminsSid);

            var candidates = new Dictionary<(string Sid, bool IsDeny), AceAccumulator>(
                SidDenyKeyComparer);

            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.IdentityReference is not SecurityIdentifier ruleSid)
                    continue;

                var ruleSidStr = ruleSid.Value;
                if (sid != null && !string.Equals(ruleSidStr, sid, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool ruleIsDeny = rule.AccessControlType == AccessControlType.Deny;
                var key = (ruleSidStr, ruleIsDeny);

                if (!candidates.TryGetValue(key, out var acc))
                    acc = new AceAccumulator(
                        IsOwner: ownerIdentity != null && ownerIdentity.Equals(ruleSid),
                        IsAdminOwner: isAdminOwner);

                candidates[key] = ruleIsDeny
                    ? acc with { DenyRights = acc.DenyRights | rule.FileSystemRights }
                    : acc with { AllowRights = acc.AllowRights | rule.FileSystemRights };
            }

            bool anyModified = false;
            foreach (var ((ruleSid, ruleIsDeny), acc) in candidates)
            {
                if (!ruleIsDeny && GrantRightsMapper.IsTraverseOnly(acc.AllowRights))
                {
                    anyModified |= UpdateTraverseEntryFromPath(ruleSid, normalized);
                    continue;
                }

                var ownerState = acc.IsOwner ? RightCheckState.Checked : RightCheckState.Unchecked;
                var rights = GrantRightsMapper.FromNtfsRights(
                    acc.AllowRights, acc.DenyRights, ruleIsDeny, isFolder, ownerState, acc.IsAdminOwner);
                anyModified |= UpdateGrantEntryFromPath(ruleSid, normalized, ruleIsDeny, rights);
            }

            return anyModified;
        }
        catch (Exception ex)
        {
            log.Warn($"UpdateFromPath failed for '{normalized}': {ex.Message}");
            return false;
        }
    }

    private bool UpdateGrantEntryFromPath(string sid, string path, bool isDeny, SavedRightsState rights)
    {
        var (modified, entry) = dbAccessor.Write(database =>
        {
            var grants = database.GetOrCreateAccount(sid).Grants;
            var entry = GrantCoreOperations.FindGrantEntryInList(grants, path, isDeny);
            if (entry != null)
            {
                if (entry.SavedRights != rights)
                {
                    entry.SavedRights = rights;
                    return (true, entry);
                }

                return (false, entry);
            }

            entry = new GrantedPathEntry { Path = path, IsDeny = isDeny, SavedRights = rights };
            grants.Add(entry);
            return (true, entry);
        });

        bool alreadyTrackedInMain = IsTrackedInMainConfig(sid, entry);
        if (!alreadyTrackedInMain)
            EnsureMainStoreMembership(sid, entry);
        return modified || !alreadyTrackedInMain;
    }

    private bool UpdateTraverseEntryFromPath(string sid, string path)
    {
        var ownerSid = ownerResolver.ResolveStorageOwnerSid(sid);
        var (modified, entry) = dbAccessor.Write(database =>
        {
            var existing = ownerResolver.FindTraverseEntry(database, ownerSid, path);
            if (existing != null)
                return (false, existing);

            var grants = database.GetOrCreateAccount(ownerSid).Grants;
            var entry = new GrantedPathEntry { Path = path, IsTraverseOnly = true };
            grants.Add(entry);
            return (true, entry);
        });

        bool alreadyTrackedInMain = IsTrackedInMainConfig(ownerSid, entry);
        if (!alreadyTrackedInMain)
            EnsureMainStoreMembership(ownerSid, entry);
        return modified || !alreadyTrackedInMain;
    }

    private readonly record struct AceAccumulator(
        FileSystemRights AllowRights = 0,
        FileSystemRights DenyRights = 0,
        bool IsOwner = false,
        bool IsAdminOwner = false);

    private static readonly GrantPathKeyComparer SidDenyKeyComparer = new();

    private bool IsTrackedInMainConfig(string ownerSid, GrantedPathEntry entry)
    {
        var locations = FindLocations(ownerSid, entry);
        return locations.Any(location => location.Store.ConfigPath == null);
    }

    private void EnsureMainStoreMembership(string ownerSid, GrantedPathEntry entry)
    {
        if (IsTrackedInMainConfig(ownerSid, entry))
            return;

        GrantIntentStoreProvider.MainStore.AddEntry(ownerSid, entry);
    }

    private IReadOnlyList<GrantIntentLocation> FindLocations(string ownerSid, GrantedPathEntry entry)
        => entry.IsTraverseOnly
            ? GrantIntentRepository.FindTraverseLocations(ownerSid, entry)
            : GrantIntentRepository.FindGrantLocations(ownerSid, entry);
}
