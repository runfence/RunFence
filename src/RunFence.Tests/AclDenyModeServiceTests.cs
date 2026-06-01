using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Integration tests for <see cref="AclDenyModeService"/> — verifies ACL predicate behavior
/// against real NTFS ACLs on temp directories.
/// Tests run as non-elevated user; all paths are in the current user's temp directory.
/// </summary>
public class AclDenyModeServiceTests : IDisposable
{
    // Fake SID for managed deny ACEs (syntactically valid, resolves to no real account)
    private const string ManagedSid = "S-1-5-21-9999999999-9999999999-9999999999-2001";

    // Fake SID for external deny ACEs (represents an "external tool" SID)
    private const string ExternalSid = "S-1-5-21-9999999999-9999999999-9999999999-2002";

    private readonly TempDirectory _tempDir = new("AclDenyModeService");
    private readonly AclDenyModeService _service;
    private readonly Mock<ILocalUserProvider> _localUserProvider = new();

    public AclDenyModeServiceTests()
    {
        _localUserProvider
            .Setup(p => p.GetLocalUserAccounts())
            .Returns([new LocalUserAccount("manageduser", ManagedSid)]);

        var dbProvider = new LambdaDatabaseProvider(() => new AppDatabase());
        var containerLookup = new ContainerLookupHelper(dbProvider);
        var iuResolver = new Mock<IInteractiveUserResolver>();
        iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);
        var acl = AclAccessorFactory.Create();
        var log = new Mock<ILoggingService>();

        _service = new AclDenyModeService(log.Object, _localUserProvider.Object, containerLookup,
            iuResolver.Object, acl, new AppEntryAclTargetResolver());
    }

    public void Dispose() => _tempDir.Dispose();

    // --- F-01-test: ApplyDenyRules filter predicate — deny ACE with non-managed bits preserved ---

    [Fact]
    public void ApplyDeny_ManagedUserDenyAceWithNonManagedBits_Preserved()
    {
        // Arrange: create a file and add a FullControl deny ACE for the MANAGED user (who IS
        // in localUsers / knownSids). FullControl has bits outside ManagedDenyRightsMask.
        //
        // The F-01 fix changes the ApplyDenyRules removal predicate from:
        //   (rule.FileSystemRights & ManagedDenyRightsMask) != 0   [overlap — wrongly removes ACE]
        // to:
        //   (rule.FileSystemRights & ~ManagedDenyRightsMask) == 0  [subset — correctly keeps ACE]
        //
        // For a FullControl deny on a knownSid with old (overlap) predicate:
        //   (FullControl & Mask) != 0 → true → ACE is selected for removal (WRONG)
        // With new (subset) predicate:
        //   (FullControl & ~Mask) == 0 → false → ACE is NOT selected for removal (CORRECT)
        //
        // To isolate the mask check, ManagedSid is put into allowedSids so no Execute deny
        // is added to desiredRules. With empty desiredRules, ApplyAclDiff only removes ACEs
        // matching the predicate — and the FullControl deny should survive because it does NOT.
        var file = Path.Combine(_tempDir.Path, "managed_deny_fullcontrol.txt");
        File.WriteAllText(file, "test");

        // Add a FullControl deny ACE for the managed user (in knownSids)
        var managedIdentity = new SecurityIdentifier(ManagedSid);
        var fileInfo = new FileInfo(file);
        var security = fileInfo.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            managedIdentity,
            FileSystemRights.FullControl, // includes bits outside ManagedDenyRightsMask
            AccessControlType.Deny));
        fileInfo.SetAccessControl(security);

        Assert.True(HasExplicitDenyAceWithRights(file, managedIdentity, FileSystemRights.FullControl),
            "FullControl deny ACE must exist before ApplyDeny");

        // ManagedSid is in allowedSids → no Execute deny added for it → desiredRules is empty.
        // ApplyAclDiff will only remove ACEs that match the predicate.
        // With the F-01 fix (subset check), FullControl → predicate false → NOT removed.
        var allowedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManagedSid };
        _service.ApplyDeny(file, isFolder: false, allowedSids, DeniedRights.Execute);

        // Assert: the FullControl deny ACE for the managed user is still present because
        // (FullControl & ~ManagedDenyRightsMask) != 0 — subset check fails, ACE kept.
        Assert.True(HasExplicitDenyAceWithRights(file, managedIdentity, FileSystemRights.FullControl),
            "FullControl deny ACE must be preserved: it has non-managed bits, so subset check excludes it");
    }

    private static bool HasExplicitDenyAceWithRights(string path, SecurityIdentifier sid, FileSystemRights rights)
    {
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        return rules.Cast<FileSystemAccessRule>().Any(rule =>
            rule.AccessControlType == AccessControlType.Deny &&
            rule.IdentityReference is SecurityIdentifier ruleSid &&
            ruleSid.Equals(sid) &&
            (rule.FileSystemRights & rights) == rights);
    }
}

