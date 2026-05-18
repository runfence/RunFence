using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Tests.TestHelpers;
using Xunit;

namespace RunFence.Tests;

public class ShortcutProtectionServiceTests
{
    [Fact]
    public void ProtectInternalShortcut_RemovesEveryoneDenyAce()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        using var cleanupDirectory = new TempDirectory("RunFence_ShortcutProtectionState");
        var stateStoreRoot = Path.Combine(cleanupDirectory.Path, "State");

        var log = new Mock<ILoggingService>();
        var service = new ShortcutProtectionService(log.Object, AclAccessorFactory.Create(), CreateStateStore(stateStoreRoot));

        service.ProtectShortcut(shortcutPath);

        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var hasEveryoneDenyBefore = scope.ReadAcl(shortcutPath)
            .GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Any(rule =>
                rule.AccessControlType == AccessControlType.Deny &&
                rule.IdentityReference is SecurityIdentifier sid &&
                sid.Equals(everyoneSid));
        Assert.True(hasEveryoneDenyBefore, "Everyone Deny should exist after ProtectShortcut");

        var accountSid = WindowsIdentity.GetCurrent().User!.Value;
        service.ProtectInternalShortcut(shortcutPath, accountSid);

        var afterRules = scope.ReadAcl(shortcutPath)
            .GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        Assert.DoesNotContain(afterRules, rule =>
            rule.AccessControlType == AccessControlType.Deny &&
            rule.IdentityReference is SecurityIdentifier sid &&
            sid.Equals(everyoneSid));
        Assert.Contains(afterRules, rule =>
            rule.AccessControlType == AccessControlType.Allow &&
            rule.IdentityReference is SecurityIdentifier sid &&
            sid.Value == accountSid);

        service.UnprotectShortcut(shortcutPath);
    }

    [Fact]
    public void UnprotectShortcut_PreservesExternalReadOnlyState()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        using var cleanupDirectory = new TempDirectory("RunFence_ShortcutProtectionState");
        var stateStoreRoot = Path.Combine(cleanupDirectory.Path, "State");

        File.SetAttributes(shortcutPath, File.GetAttributes(shortcutPath) | FileAttributes.ReadOnly);
        var service = new ShortcutProtectionService(new Mock<ILoggingService>().Object, AclAccessorFactory.Create(), CreateStateStore(stateStoreRoot));

        service.ProtectShortcut(shortcutPath);
        service.UnprotectShortcut(shortcutPath);

        Assert.True((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
    }

    [Fact]
    public void UnprotectShortcut_PreservesExternalEveryoneDenyAce_WhenRunFenceDidNotAddIt()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        using var cleanupDirectory = new TempDirectory("RunFence_ShortcutProtectionState");
        var stateStoreRoot = Path.Combine(cleanupDirectory.Path, "State");
        const FileSystemRights externalDenyRights = FileSystemRights.ReadPermissions;

        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var security = scope.ReadAcl(shortcutPath);
        security.AddAccessRule(new FileSystemAccessRule(
            everyoneSid,
            externalDenyRights,
            AccessControlType.Deny));
        new FileInfo(shortcutPath).SetAccessControl(security);

        var service = new ShortcutProtectionService(new Mock<ILoggingService>().Object, AclAccessorFactory.Create(), CreateStateStore(stateStoreRoot));

        service.ProtectShortcut(shortcutPath);
        service.UnprotectShortcut(shortcutPath);

        Assert.True(HasAce(shortcutPath, everyoneSid.Value,
            externalDenyRights,
            AccessControlType.Deny));
    }

    [Fact]
    public void ProtectShortcut_SetsReadOnlyBeforeAddingManagedDenyAce()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        using var cleanupDirectory = new TempDirectory("RunFence_ShortcutProtectionState");
        var stateStoreRoot = Path.Combine(cleanupDirectory.Path, "State");

        var aclAccessor = new Mock<IAclAccessor>();
        aclAccessor.Setup(a => a.ModifyAclWithFallback(
                shortcutPath,
                false,
                It.IsAny<Func<FileSystemSecurity, bool>>()))
            .Callback<string, bool, Func<FileSystemSecurity, bool>>((_, _, modify) =>
            {
                Assert.True((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
                modify(new FileSecurity());
            })
            .Returns(true);
        var service = new ShortcutProtectionService(new Mock<ILoggingService>().Object, aclAccessor.Object, CreateStateStore(stateStoreRoot));

        service.ProtectShortcut(shortcutPath);

        aclAccessor.Verify(a => a.ModifyAclWithFallback(
            shortcutPath,
            false,
            It.IsAny<Func<FileSystemSecurity, bool>>()), Times.Once);
    }

    [Fact]
    public void ProtectInternalShortcut_SetsReadOnlyBeforeReplacingAcl()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        using var cleanupDirectory = new TempDirectory("RunFence_ShortcutProtectionState");
        var stateStoreRoot = Path.Combine(cleanupDirectory.Path, "State");

        var aclAccessor = new Mock<IAclAccessor>();
        aclAccessor.Setup(a => a.ModifyAclWithFallback(
                shortcutPath,
                false,
                It.IsAny<Func<FileSystemSecurity, bool>>()))
            .Callback<string, bool, Func<FileSystemSecurity, bool>>((_, _, modify) =>
            {
                Assert.True((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
                modify(new FileSecurity());
            })
            .Returns(true);
        var service = new ShortcutProtectionService(new Mock<ILoggingService>().Object, aclAccessor.Object, CreateStateStore(stateStoreRoot));

        service.ProtectInternalShortcut(shortcutPath, WindowsIdentity.GetCurrent().User!.Value);

        aclAccessor.Verify(a => a.ModifyAclWithFallback(
            shortcutPath,
            false,
            It.IsAny<Func<FileSystemSecurity, bool>>()), Times.Once);
    }

    [Fact]
    public void ProtectShortcut_WhenAclWriteFailsAfterReadOnly_RestoresReadOnlyState()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        using var cleanupDirectory = new TempDirectory("RunFence_ShortcutProtectionState");
        var stateStoreRoot = Path.Combine(cleanupDirectory.Path, "State");
        var aclException = new IOException("acl failed");

        var aclAccessor = new Mock<IAclAccessor>();
        aclAccessor.Setup(a => a.ModifyAclWithFallback(
                shortcutPath,
                false,
                It.IsAny<Func<FileSystemSecurity, bool>>()))
            .Throws(aclException);
        var service = new ShortcutProtectionService(new Mock<ILoggingService>().Object, aclAccessor.Object, CreateStateStore(stateStoreRoot));

        var ex = Assert.Throws<ShortcutProtectionException>(() => service.ProtectShortcut(shortcutPath));

        Assert.Equal("apply", ex.Operation);
        Assert.Same(aclException, ex.InnerException);
        Assert.False((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
        Assert.Empty(Directory.GetFiles(stateStoreRoot));
    }

    [Fact]
    public void UnprotectShortcut_RemovesRunFenceManagedDenyAce()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        using var cleanupDirectory = new TempDirectory("RunFence_ShortcutProtectionState");
        var stateStoreRoot = Path.Combine(cleanupDirectory.Path, "State");

        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var service = new ShortcutProtectionService(new Mock<ILoggingService>().Object, AclAccessorFactory.Create(), CreateStateStore(stateStoreRoot));

        service.ProtectShortcut(shortcutPath);
        Assert.True(HasAce(shortcutPath, everyoneSid.Value,
            FileSystemRights.Delete | FileSystemRights.Write | FileSystemRights.WriteAttributes | FileSystemRights.AppendData,
            AccessControlType.Deny));

        service.UnprotectShortcut(shortcutPath);

        Assert.False(HasAce(shortcutPath, everyoneSid.Value,
            FileSystemRights.Delete | FileSystemRights.Write | FileSystemRights.WriteAttributes | FileSystemRights.AppendData,
            AccessControlType.Deny));
    }

    [Fact]
    public void ProtectShortcut_IdempotentCall_PreservesRunFenceOwnershipState()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        using var cleanupDirectory = new TempDirectory("RunFence_ShortcutProtectionState");
        var stateStoreRoot = Path.Combine(cleanupDirectory.Path, "State");

        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var service = new ShortcutProtectionService(new Mock<ILoggingService>().Object, AclAccessorFactory.Create(), CreateStateStore(stateStoreRoot));

        service.ProtectShortcut(shortcutPath);
        service.ProtectShortcut(shortcutPath);
        service.UnprotectShortcut(shortcutPath);

        Assert.False(HasAce(shortcutPath, everyoneSid.Value,
            FileSystemRights.Delete | FileSystemRights.Write | FileSystemRights.WriteAttributes | FileSystemRights.AppendData,
            AccessControlType.Deny));
    }

    [Fact]
    public void ProtectShortcut_WhenAdministratorsDeleteIsAllowed_DoesNotAddManagedDenyAce()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        using var cleanupDirectory = new TempDirectory("RunFence_ShortcutProtectionState");
        var stateStoreRoot = Path.Combine(cleanupDirectory.Path, "State");

        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var service = new ShortcutProtectionService(new Mock<ILoggingService>().Object, AclAccessorFactory.Create(), CreateStateStore(stateStoreRoot));

        service.ProtectShortcut(shortcutPath, allowAdministratorsDelete: true);

        Assert.False(HasAce(shortcutPath, everyoneSid.Value,
            FileSystemRights.Delete | FileSystemRights.Write | FileSystemRights.WriteAttributes | FileSystemRights.AppendData,
            AccessControlType.Deny));
        Assert.True((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
    }

    [Fact]
    public void ProtectShortcut_WhenAdministratorsDeleteIsAllowed_RemovesExistingRunFenceManagedDenyAce()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        using var cleanupDirectory = new TempDirectory("RunFence_ShortcutProtectionState");
        var stateStoreRoot = Path.Combine(cleanupDirectory.Path, "State");

        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var service = new ShortcutProtectionService(new Mock<ILoggingService>().Object, AclAccessorFactory.Create(), CreateStateStore(stateStoreRoot));

        service.ProtectShortcut(shortcutPath);
        Assert.True(HasAce(shortcutPath, everyoneSid.Value,
            FileSystemRights.Delete | FileSystemRights.Write | FileSystemRights.WriteAttributes | FileSystemRights.AppendData,
            AccessControlType.Deny));

        service.ProtectShortcut(shortcutPath, allowAdministratorsDelete: true);

        Assert.False(HasAce(shortcutPath, everyoneSid.Value,
            FileSystemRights.Delete | FileSystemRights.Write | FileSystemRights.WriteAttributes | FileSystemRights.AppendData,
            AccessControlType.Deny));
    }

    [Fact]
    public void ProtectShortcut_StateSaveFails_DoesNotWriteAcl()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");

        var log = new Mock<ILoggingService>();
        var aclAccessor = new Mock<IAclAccessor>(MockBehavior.Strict);
        var stateStore = new Mock<IShortcutProtectionStateStore>(MockBehavior.Strict);
        stateStore.Setup(x => x.Load(shortcutPath)).Returns((ShortcutProtectionState?)null);
        stateStore.Setup(x => x.Save(It.IsAny<ShortcutProtectionState>())).Throws(new IOException("save failed"));
        var service = new ShortcutProtectionService(log.Object, aclAccessor.Object, stateStore.Object);

        var ex = Assert.Throws<ShortcutProtectionException>(() => service.ProtectShortcut(shortcutPath));

        Assert.Equal("persist", ex.Operation);
        Assert.Empty(aclAccessor.Invocations);
        Assert.False((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
    }

    [Fact]
    public void ProtectInternalShortcut_StateSaveFails_DoesNotWriteAcl()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");

        var log = new Mock<ILoggingService>();
        var aclAccessor = new Mock<IAclAccessor>(MockBehavior.Strict);
        var stateStore = new Mock<IShortcutProtectionStateStore>(MockBehavior.Strict);
        stateStore.Setup(x => x.Load(shortcutPath)).Returns((ShortcutProtectionState?)null);
        stateStore.Setup(x => x.Save(It.IsAny<ShortcutProtectionState>())).Throws(new IOException("save failed"));
        var service = new ShortcutProtectionService(log.Object, aclAccessor.Object, stateStore.Object);

        var ex = Assert.Throws<ShortcutProtectionException>(() =>
            service.ProtectInternalShortcut(shortcutPath, WindowsIdentity.GetCurrent().User!.Value));

        Assert.Equal("persist", ex.Operation);
        Assert.Empty(aclAccessor.Invocations);
        Assert.False((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
    }

    private static bool HasAce(string path, string sid, FileSystemRights rights, AccessControlType type)
    {
        var rules = new FileInfo(path)
            .GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference is SecurityIdentifier identity &&
                identity.Value == sid &&
                rule.AccessControlType == type &&
                (rule.FileSystemRights & rights) == rights)
            {
                return true;
            }
        }

        return false;
    }
    private static IShortcutProtectionStateStore CreateStateStore(string stateStoreRootPath)
        => new ShortcutProtectionStateStore(stateStoreRootPath);
}

