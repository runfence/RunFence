using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Tests.TestHelpers;
using Xunit;

namespace RunFence.Tests;

public class ShortcutProtectionServiceTests
{
    private const string AppId = "app1";

    [Fact]
    public void ProtectInternalShortcut_RemovesEveryoneDenyAce()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        var service = CreateService();

        service.ProtectShortcut(AppId, shortcutPath);

        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        Assert.True(HasAce(
            shortcutPath,
            everyoneSid.Value,
            ShortcutManagedDenyAceEditor.ManagedDenyRights,
            AccessControlType.Deny));

        var accountSid = WindowsIdentity.GetCurrent().User!.Value;
        service.ProtectInternalShortcut(AppId, shortcutPath, accountSid);

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

        service.UnprotectShortcut(AppId, shortcutPath);
    }

    [Fact]
    public void UnprotectShortcut_PreservesExternalReadOnlyState()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        File.SetAttributes(shortcutPath, File.GetAttributes(shortcutPath) | FileAttributes.ReadOnly);
        var service = CreateService();

        service.ProtectShortcut(AppId, shortcutPath);
        service.UnprotectShortcut(AppId, shortcutPath);

        Assert.True((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
    }

    [Fact]
    public void UnprotectShortcut_ThrowsAndPreservesState_WhenExternalEveryoneDenyAceKeepsShortcutProtected()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        const FileSystemRights externalDenyRights = FileSystemRights.ReadPermissions;

        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var security = scope.ReadAcl(shortcutPath);
        security.AddAccessRule(new FileSystemAccessRule(everyoneSid, externalDenyRights, AccessControlType.Deny));
        new FileInfo(shortcutPath).SetAccessControl(security);

        var stateStore = CreateStateStore();
        var service = CreateService(stateStore: stateStore);

        service.ProtectShortcut(AppId, shortcutPath);
        var ex = Assert.Throws<ShortcutProtectionException>(() => service.UnprotectShortcut(AppId, shortcutPath));

        Assert.Equal("remove", ex.Operation);
        Assert.True(HasAce(shortcutPath, everyoneSid.Value, externalDenyRights, AccessControlType.Deny));
        Assert.NotNull(stateStore.Load(AppId, shortcutPath));
    }

    [Fact]
    public void ProtectShortcut_SetsReadOnlyBeforeAddingManagedDenyAce()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        var aclAccessor = new Mock<IPathSecurityDescriptorAccessor>();
        aclAccessor.Setup(a => a.GetSecurity(shortcutPath)).Returns(new FileSecurity());
        aclAccessor.Setup(a => a.ModifyAclWithFallback(
                shortcutPath,
                It.IsAny<Func<FileSystemSecurity, bool>>()))
            .Callback<string, Func<FileSystemSecurity, bool>>((_, modify) =>
            {
                Assert.True((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
                modify(new FileSecurity());
            })
            .Returns(true);
        var service = CreateService(aclAccessor: aclAccessor.Object);

        service.ProtectShortcut(AppId, shortcutPath);

        aclAccessor.Verify(a => a.ModifyAclWithFallback(
            shortcutPath,
            It.IsAny<Func<FileSystemSecurity, bool>>()), Times.Once);
    }

    [Fact]
    public void ProtectInternalShortcut_SetsReadOnlyBeforeReplacingAcl()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        var aclAccessor = new Mock<IPathSecurityDescriptorAccessor>();
        aclAccessor.Setup(a => a.ModifyAclWithFallback(
                shortcutPath,
                It.IsAny<Func<FileSystemSecurity, bool>>()))
            .Callback<string, Func<FileSystemSecurity, bool>>((_, modify) =>
            {
                Assert.True((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
                modify(new FileSecurity());
            })
            .Returns(true);
        var service = CreateService(aclAccessor: aclAccessor.Object);

        service.ProtectInternalShortcut(
            AppId,
            shortcutPath,
            WindowsIdentity.GetCurrent().User!.Value);

        aclAccessor.Verify(a => a.ModifyAclWithFallback(
            shortcutPath,
            It.IsAny<Func<FileSystemSecurity, bool>>()), Times.Once);
    }

    [Fact]
    public void ProtectShortcut_WhenAclWriteFailsAfterReadOnly_RestoresReadOnlyState()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        var aclException = new IOException("acl failed");

        var aclAccessor = new Mock<IPathSecurityDescriptorAccessor>();
        aclAccessor.Setup(a => a.GetSecurity(shortcutPath)).Returns(new FileSecurity());
        aclAccessor.Setup(a => a.ModifyAclWithFallback(
                shortcutPath,
                It.IsAny<Func<FileSystemSecurity, bool>>()))
            .Throws(aclException);
        var stateStore = CreateStateStore();
        var service = CreateService(aclAccessor.Object, stateStore);

        var ex = Assert.Throws<ShortcutProtectionException>(() =>
            service.ProtectShortcut(AppId, shortcutPath));

        Assert.Equal("apply", ex.Operation);
        Assert.Same(aclException, ex.InnerException);
        Assert.False((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
        Assert.Null(stateStore.Load(AppId, shortcutPath));
    }

    [Fact]
    public void ProtectShortcut_WithPreExistingManagedDenyAce_DoesNotPersistDenyOwnership()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        AddManagedDenyAce(scope, shortcutPath);
        var stateStore = CreateStateStore();
        var service = CreateService(stateStore: stateStore);

        service.ProtectShortcut(AppId, shortcutPath);
        service.UnprotectShortcut(AppId, shortcutPath);

        Assert.True(HasManagedDenyAce(shortcutPath));
        Assert.Null(stateStore.Load(AppId, shortcutPath));
    }

    [Fact]
    public void ProtectShortcut_WithPreExistingReadOnlyAndManagedDenyAce_DoesNotPersistExternalOwnership()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        File.SetAttributes(shortcutPath, File.GetAttributes(shortcutPath) | FileAttributes.ReadOnly);
        AddManagedDenyAce(scope, shortcutPath);
        var stateStore = CreateStateStore();
        var service = CreateService(stateStore: stateStore);

        service.ProtectShortcut(AppId, shortcutPath);
        service.UnprotectShortcut(AppId, shortcutPath);

        Assert.True((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
        Assert.True(HasManagedDenyAce(shortcutPath));
        Assert.Null(stateStore.Load(AppId, shortcutPath));
    }

    [Fact]
    public void ProtectShortcut_ExistingReadOnlyAndManagedDenyWithoutState_DoesNotPersistOwnership()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        File.SetAttributes(shortcutPath, File.GetAttributes(shortcutPath) | FileAttributes.ReadOnly);
        AddManagedDenyAce(scope, shortcutPath);
        var stateStore = CreateStateStore();
        var service = CreateService(stateStore: stateStore);

        service.ProtectShortcut(AppId, shortcutPath);

        var state = stateStore.Load(AppId, shortcutPath);
        Assert.Null(state);

        service.UnprotectShortcut(AppId, shortcutPath);

        Assert.True(HasManagedDenyAce(shortcutPath));
        Assert.True((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
    }

    [Fact]
    public void ProtectShortcut_ExistingReadOnlyWithoutState_DoesNotRecoverReadOnlyOwnership()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        File.SetAttributes(shortcutPath, File.GetAttributes(shortcutPath) | FileAttributes.ReadOnly);
        var stateStore = CreateStateStore();
        var service = CreateService(stateStore: stateStore);

        service.ProtectShortcut(AppId, shortcutPath);

        var state = stateStore.Load(AppId, shortcutPath);
        Assert.NotNull(state);
        Assert.False(state.ReadOnlySetByRunFence);

        service.UnprotectShortcut(AppId, shortcutPath);

        Assert.True((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
    }

    [Fact]
    public void ProtectInternalShortcut_InternalProtectedAclWithoutState_DoesNotRecoverReadOnlyOwnership()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        var accountSid = WindowsIdentity.GetCurrent().User!.Value;
        CreateInternalProtectedAcl(shortcutPath, accountSid);
        var stateStore = CreateStateStore();
        var service = CreateService(stateStore: stateStore);

        service.ProtectInternalShortcut(AppId, shortcutPath, accountSid);

        var state = stateStore.Load(AppId, shortcutPath);
        Assert.Null(state);

        service.UnprotectShortcut(AppId, shortcutPath);

        Assert.True((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
    }

    [Fact]
    public void ProtectShortcut_WhenAdministratorsDeleteIsAllowed_RemovesOwnedManagedDenyAce_AndClearsPersistedOwnership()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");
        var stateStore = CreateStateStore();
        var service = CreateService(stateStore: stateStore);

        service.ProtectShortcut(AppId, shortcutPath);

        service.ProtectShortcut(
            AppId,
            shortcutPath,
            allowAdministratorsDelete: true);

        Assert.False(HasManagedDenyAce(shortcutPath));
        var state = stateStore.Load(AppId, shortcutPath);
        Assert.NotNull(state);
        Assert.False(state.ManagedDenyAceApplied);
        Assert.True(state.ReadOnlySetByRunFence);
    }

    [Fact]
    public void ProtectShortcut_StateSaveFails_DoesNotWriteAcl()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");

        var aclAccessor = new Mock<IPathSecurityDescriptorAccessor>(MockBehavior.Strict);
        aclAccessor.Setup(x => x.GetSecurity(shortcutPath)).Returns(new FileSecurity());
        var stateStore = new Mock<IShortcutProtectionStateStore>(MockBehavior.Strict);
        stateStore.Setup(x => x.Load(AppId, shortcutPath)).Returns((ShortcutProtectionState?)null);
        stateStore.Setup(x => x.Save(AppId, It.IsAny<ShortcutProtectionState>())).Throws(new IOException("save failed"));
        var service = CreateService(aclAccessor.Object, stateStore.Object);

        var ex = Assert.Throws<ShortcutProtectionException>(() =>
            service.ProtectShortcut(AppId, shortcutPath));

        Assert.Equal("persist", ex.Operation);
        aclAccessor.Verify(a => a.ModifyAclWithFallback(shortcutPath, It.IsAny<Func<FileSystemSecurity, bool>>()), Times.Never);
        Assert.False((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
    }

    [Fact]
    public void ProtectInternalShortcut_StateSaveFails_DoesNotWriteAcl()
    {
        using var scope = new ShortcutAclTestScope();
        var shortcutPath = scope.CreateShortcut(@"C:\Target\App.exe", "");

        var aclAccessor = new Mock<IPathSecurityDescriptorAccessor>(MockBehavior.Strict);
        var stateStore = new Mock<IShortcutProtectionStateStore>(MockBehavior.Strict);
        stateStore.Setup(x => x.Load(AppId, shortcutPath)).Returns((ShortcutProtectionState?)null);
        stateStore.Setup(x => x.Save(AppId, It.IsAny<ShortcutProtectionState>())).Throws(new IOException("save failed"));
        var service = CreateService(aclAccessor.Object, stateStore.Object);

        var ex = Assert.Throws<ShortcutProtectionException>(() =>
            service.ProtectInternalShortcut(
                AppId,
                shortcutPath,
                WindowsIdentity.GetCurrent().User!.Value));

        Assert.Equal("persist", ex.Operation);
        aclAccessor.Verify(a => a.ModifyAclWithFallback(shortcutPath, It.IsAny<Func<FileSystemSecurity, bool>>()), Times.Never);
        Assert.False((File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0);
    }

    private static ShortcutProtectionService CreateService(
        IPathSecurityDescriptorAccessor? aclAccessor = null,
        IShortcutProtectionStateStore? stateStore = null,
        ILoggingService? log = null)
        => ShortcutProtectionTestFactory.Create(log, aclAccessor, stateStore);

    private static IShortcutProtectionStateStore CreateStateStore()
        => new InMemoryShortcutProtectionStateStore();

    private static void AddManagedDenyAce(ShortcutAclTestScope scope, string path)
    {
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var security = scope.ReadAcl(path);
        security.AddAccessRule(new FileSystemAccessRule(
            everyoneSid,
            ShortcutManagedDenyAceEditor.ManagedDenyRights | FileSystemRights.Synchronize,
            AccessControlType.Deny));
        new FileInfo(path).SetAccessControl(security);
    }

    private static void CreateInternalProtectedAcl(string path, string accountSid)
    {
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl | FileSystemRights.Synchronize,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl | FileSystemRights.Synchronize,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(accountSid),
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
            AccessControlType.Allow));
        var currentMockSid = AdminOperationMockAccessHelper.GetCurrentProcessSidWhenUsingMocks();
        if (currentMockSid != null)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                currentMockSid,
                FileSystemRights.FullControl | FileSystemRights.Synchronize,
                AccessControlType.Allow));
        }

        new FileInfo(path).SetAccessControl(security);
    }

    private static bool HasManagedDenyAce(string path)
    {
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null).Value;
        return HasAce(path, everyoneSid, ShortcutManagedDenyAceEditor.ManagedDenyRights, AccessControlType.Deny);
    }

    private static bool HasAce(string path, string sid, FileSystemRights rights, AccessControlType type)
    {
        var rules = new FileInfo(path)
            .GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            var normalizedRights = type == AccessControlType.Deny
                ? rule.FileSystemRights & ~FileSystemRights.Synchronize
                : rule.FileSystemRights & ~FileSystemRights.Synchronize;
            if (rule.IdentityReference is SecurityIdentifier identity &&
                identity.Value == sid &&
                rule.AccessControlType == type &&
                (normalizedRights & rights) == rights)
            {
                return true;
            }
        }

        return false;
    }
}
