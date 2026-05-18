using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.IntegrationTests;

public sealed class ShortcutPersistenceSmokeTests
{
    [ElevatedFact]
    public void ShortcutService_UsesProductionTrustedTempPersistence_ForNewAndProtectedExistingShortcutWrites()
    {
        using var root = new TempDirectory("RunFence_ShortcutPersistenceSmoke");
        var trustedTempPath = System.IO.Path.Combine(root.Path, "trusted-temp");
        Directory.CreateDirectory(trustedTempPath);

        var shortcutPath = System.IO.Path.Combine(root.Path, "Managed App.lnk");
        var stateStorePath = System.IO.Path.Combine(root.Path, "protection-state");
        var iconPath = System.IO.Path.Combine(root.Path, "managed.ico");
        File.WriteAllBytes(iconPath, []);
        var shortcutHelper = new ShortcutComHelper();
        var protection = new ShortcutProtectionService(
            new IntegrationTestLoggingService(),
            CreateAclAccessor(),
            new ShortcutProtectionStateStore(stateStorePath));
        var persistenceNative = new FailingOnceShortcutFilePersistenceNative(
            new ShortcutFilePersistenceNative(
                new BackupPrivilegeSecurityDescriptorAccessor(new BackupPrivilegeSecurityNative())));
        var writeAccessService = new ShortcutWriteAccessService(
            new ShortcutFilePersistenceService(shortcutHelper, persistenceNative, trustedTempPath));
        var app = new AppEntry
        {
            Id = "create-app-id",
            ExePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0],
            ManageShortcuts = true
        };
        var shortcutService = new ShortcutService(
            new IntegrationTestLoggingService(),
            new IntegrationTestIconService(iconPath),
            protection,
            writeAccessService,
            shortcutHelper,
            new IntegrationTestInteractiveUserDesktopProvider(),
            new ShortcutFinder());
        try
        {
            shortcutService.SaveShortcut(app, shortcutPath);

            ShortcutPersistenceTestAssertions.AssertShortcut(
                shortcutHelper,
                shortcutPath,
                Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName),
                app.Id,
                Path.GetDirectoryName(Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName)));
            Assert.Equal(1, persistenceNative.PublishAttempts);
            Assert.Equal(0, persistenceNative.DeleteInvocations);
            Assert.Equal(0, persistenceNative.DeleteExistingFileAttempts);
            Assert.True(File.GetAttributes(shortcutPath).HasFlag(FileAttributes.ReadOnly));
            Assert.True(ShortcutPersistenceTestAssertions.HasManagedEveryoneDenyAce(shortcutPath));
            var createdShortcutIdentity = ShortcutPersistenceTestAssertions.ReadFileIdentity(shortcutPath);

            var launcherPath = Path.Combine(root.Path, "Launcher", "RunFence.Launcher.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(launcherPath)!);
            File.WriteAllBytes(launcherPath, []);
            var cache = new ShortcutTraversalCache(
            [
                new ShortcutTraversalEntry(
                    shortcutPath,
                    Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName),
                    app.Id)
            ]);
            shortcutService.EnforceShortcuts([app], launcherPath, cache);
            var updatedFileIdentity = ShortcutPersistenceTestAssertions.ReadFileIdentity(shortcutPath);

            ShortcutPersistenceTestAssertions.AssertShortcut(
                shortcutHelper,
                shortcutPath,
                launcherPath,
                app.Id,
                Path.GetDirectoryName(launcherPath));
            Assert.NotEqual(createdShortcutIdentity, updatedFileIdentity);
            Assert.True(File.GetAttributes(shortcutPath).HasFlag(FileAttributes.ReadOnly));
            Assert.True(ShortcutPersistenceTestAssertions.HasManagedEveryoneDenyAce(shortcutPath));
            Assert.Equal(3, persistenceNative.PublishAttempts);
            Assert.Equal(3, persistenceNative.DeleteInvocations);
            Assert.Equal(1, persistenceNative.DeleteExistingFileAttempts);
            Assert.Equal(1, persistenceNative.FailedPublishAttempts);
            Assert.Empty(Directory.GetFiles(trustedTempPath, "*.lnk", SearchOption.AllDirectories));
        }
        finally
        {
            ShortcutPersistenceTestAssertions.TryDeleteShortcut(persistenceNative, shortcutPath);
        }
    }

    [ElevatedFact]
    public void ShortcutService_ProtectedExistingShortcutWithoutRestorePrivilege_ThrowsAccessDeniedExceptionChain()
    {
        using var root = new TempDirectory("RunFence_ShortcutPersistence_NoRestorePrivilege");
        var trustedTempPath = Path.Combine(root.Path, "trusted-temp");
        Directory.CreateDirectory(trustedTempPath);

        var shortcutPath = Path.Combine(root.Path, "Managed App.lnk");
        var stateStorePath = Path.Combine(root.Path, "protection-state");
        var iconPath = Path.Combine(root.Path, "managed.ico");
        File.WriteAllBytes(iconPath, []);
        var shortcutHelper = new ShortcutComHelper();
        var protection = new ShortcutProtectionService(
            new IntegrationTestLoggingService(),
            CreateAclAccessor(),
            new ShortcutProtectionStateStore(stateStorePath));
        var persistenceNative = new ShortcutFilePersistenceNative(
            new BackupPrivilegeSecurityDescriptorAccessor(new BackupPrivilegeSecurityNative()));
        var writeAccessService = new ShortcutWriteAccessService(
            new ShortcutFilePersistenceService(shortcutHelper, persistenceNative, trustedTempPath));
        var app = new AppEntry
        {
            Id = "no-restore-app-id",
            ExePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0],
            ManageShortcuts = true
        };
        var shortcutService = new ShortcutService(
            new IntegrationTestLoggingService(),
            new IntegrationTestIconService(iconPath),
            protection,
            writeAccessService,
            shortcutHelper,
            new IntegrationTestInteractiveUserDesktopProvider(),
            new ShortcutFinder());
        try
        {
            shortcutService.SaveShortcut(app, shortcutPath);
            var originalDefinition = shortcutHelper.GetShortcutDefinition(shortcutPath);

            var launcherPath = Path.Combine(root.Path, "Launcher", "RunFence.Launcher.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(launcherPath)!);
            File.WriteAllBytes(launcherPath, []);
            var cache = new ShortcutTraversalCache(
            [
                new ShortcutTraversalEntry(
                    shortcutPath,
                    originalDefinition.TargetPath,
                    app.Id)
            ]);

            using var restorePrivilegeScope = new CurrentProcessPrivilegeScope(TokenPrivilegeHelper.SeRestorePrivilege, enableOnDispose: true);

            var ex = Assert.Throws<ShortcutEnforcementException>(() =>
                shortcutService.EnforceShortcuts([app], launcherPath, cache));

            Assert.NotNull(ex.InnerException);
            Assert.Contains(shortcutPath, ex.Message);
            Assert.True(ShortcutPersistenceTestAssertions.HasAccessDeniedCause(ex));
            Assert.True(ex.Causes.Count > 0);

            var finalDefinition = shortcutHelper.GetShortcutDefinition(shortcutPath);
            Assert.Equal(originalDefinition.TargetPath, finalDefinition.TargetPath);
            Assert.Equal(originalDefinition.Arguments, finalDefinition.Arguments);
            Assert.True(File.GetAttributes(shortcutPath).HasFlag(FileAttributes.ReadOnly));
            Assert.True(ShortcutPersistenceTestAssertions.HasManagedEveryoneDenyAce(shortcutPath));
        }
        finally
        {
            ShortcutPersistenceTestAssertions.TryDeleteShortcut(persistenceNative, shortcutPath);
        }
    }

    private static AclAccessor CreateAclAccessor()
        => new(new BackupPrivilegeSecurityDescriptorAccessor(new BackupPrivilegeSecurityNative()));
}
