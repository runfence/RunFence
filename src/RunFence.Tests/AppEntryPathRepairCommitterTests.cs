using Moq;
using RunFence.Acl;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public sealed class AppEntryPathRepairCommitterTests : IDisposable
{
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<IInteractiveUserDesktopProvider> _desktopProvider = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly SessionContext _session;
    private readonly TestRunFenceLauncherPathProvider _launcherPathProvider = new(
        @"C:\RunFence\RunFence.Launcher.exe",
        exists: true);

    public AppEntryPathRepairCommitterTests()
    {
        _session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        _sidNameCache.Setup(cache => cache.GetDisplayName("S-1-5-21-1")).Returns("Alice");
        _iconService.Setup(service => service.GetIconPath(It.IsAny<string>())).Returns("stored.ico");
        _iconService.Setup(service => service.CreateBadgedIcon(It.IsAny<AppEntry>())).Returns("icon.ico");
    }

    [Fact]
    public void Commit_Success_UsesOldPathForRevertAndRepairedPathForApply()
    {
        var oldPath = CreateTempFile("old.exe");
        var repairedPath = CreateTempFile("new.exe");
        var app = new AppEntry
        {
            Id = "app1",
            Name = "App",
            ExePath = oldPath,
            AccountSid = "S-1-5-21-1",
            RestrictAcl = true
        };
        _session.Database.Apps.Add(app);
        string? revertedPath = null;
        string? revertedListPath = null;
        string? appliedPath = null;
        string? appliedListPath = null;
        _aclService.Setup(service => service.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<AppEntry, IReadOnlyList<AppEntry>>((candidate, apps) =>
            {
                revertedPath = candidate.ExePath;
                revertedListPath = apps.Single().ExePath;
            });
        _aclService.Setup(service => service.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<AppEntry, IReadOnlyList<AppEntry>>((candidate, apps) =>
            {
                appliedPath = candidate.ExePath;
                appliedListPath = apps.Single().ExePath;
            });

        var committer = CreateCommitter();

        var result = committer.Commit(
            app,
            new VersionedPathRepairResult(
                repairedPath,
                Path.GetDirectoryName(oldPath)!,
                Path.GetDirectoryName(repairedPath)!,
                "app-1.0",
                "app-1.1"));

        Assert.True(result.Repaired);
        Assert.False(result.SaveFailed);
        Assert.Equal(repairedPath, app.ExePath);
        Assert.Equal(File.GetLastWriteTimeUtc(repairedPath), app.LastKnownExeTimestamp);

        Assert.Equal(oldPath, revertedPath);
        Assert.Equal(oldPath, revertedListPath);
        Assert.Equal(repairedPath, appliedPath);
        Assert.Equal(repairedPath, appliedListPath);
        _aclService.Verify(service => service.RecomputeAllAncestorAcls(
            It.Is<IReadOnlyList<AppEntry>>(apps => apps.Single().ExePath == oldPath)), Times.Once);
        _aclService.Verify(service => service.RecomputeAllAncestorAcls(
            It.Is<IReadOnlyList<AppEntry>>(apps => apps.Single().ExePath == repairedPath)), Times.Once);
        _besideTargetShortcutService.Verify(service => service.RemoveBesideTargetShortcut(
            It.Is<AppEntry>(candidate => candidate.ExePath == oldPath)), Times.Once);
        _besideTargetShortcutService.Verify(service => service.CreateBesideTargetShortcut(
            It.Is<AppEntry>(candidate => candidate.ExePath == repairedPath),
            _launcherPathProvider.GetLauncherPath(),
            "icon.ico",
            "Alice"), Times.Once);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            "app1",
            _session.Database,
            _session.PinDerivedKey,
            _session.CredentialStore.ArgonSalt), Times.Once);
    }

    [Fact]
    public void Commit_SaveFailure_RestoresPreviousAppStateAndEnforcement()
    {
        var oldPath = CreateTempFile("old-fail.exe");
        var repairedPath = CreateTempFile("new-fail.exe");
        var previousTimestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var app = new AppEntry
        {
            Id = "app1",
            Name = "App",
            ExePath = oldPath,
            AccountSid = "S-1-5-21-1",
            RestrictAcl = true,
            LastKnownExeTimestamp = previousTimestamp
        };
        _session.Database.Apps.Add(app);
        string? revertedPath = null;
        string? revertedListPath = null;
        string? restoredPath = null;
        string? restoredListPath = null;
        _aclService.Setup(service => service.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<AppEntry, IReadOnlyList<AppEntry>>((candidate, apps) =>
            {
                revertedPath = candidate.ExePath;
                revertedListPath = apps.Single().ExePath;
            });
        _aclService.Setup(service => service.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<AppEntry, IReadOnlyList<AppEntry>>((candidate, apps) =>
            {
                restoredPath = candidate.ExePath;
                restoredListPath = apps.Single().ExePath;
            });
        _appConfigService.Setup(service => service.SaveConfigForApp(
                It.IsAny<string>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("save failed"));

        var committer = CreateCommitter();

        var result = committer.Commit(
            app,
            new VersionedPathRepairResult(
                repairedPath,
                Path.GetDirectoryName(oldPath)!,
                Path.GetDirectoryName(repairedPath)!,
                "app-1.0",
                "app-1.1"));

        Assert.False(result.Repaired);
        Assert.True(result.SaveFailed);
        Assert.Equal(oldPath, app.ExePath);
        Assert.Equal(previousTimestamp, app.LastKnownExeTimestamp);
        Assert.Contains("save failed", result.WarningMessage, StringComparison.Ordinal);

        Assert.Equal(oldPath, revertedPath);
        Assert.Equal(oldPath, revertedListPath);
        Assert.Equal(oldPath, restoredPath);
        Assert.Equal(oldPath, restoredListPath);
        _aclService.Verify(service => service.RecomputeAllAncestorAcls(
            It.Is<IReadOnlyList<AppEntry>>(apps => apps.Single().ExePath == oldPath)), Times.Exactly(2));
        _besideTargetShortcutService.Verify(service => service.RemoveBesideTargetShortcut(
            It.Is<AppEntry>(candidate => candidate.ExePath == oldPath)), Times.Once);
        _besideTargetShortcutService.Verify(service => service.CreateBesideTargetShortcut(
            It.Is<AppEntry>(candidate => candidate.ExePath == oldPath),
            _launcherPathProvider.GetLauncherPath(),
            "stored.ico",
            "Alice"), Times.Once);
        _iconService.Verify(service => service.GetIconPath(It.Is<string>(id => id == "app1")), Times.Once);
        _iconService.Verify(service => service.CreateBadgedIcon(It.Is<AppEntry>(candidate => candidate.ExePath == oldPath), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Commit_SaveFailure_RestoreEnforcementFailure_ReturnsCombinedWarningWithoutRefreshingIcon()
    {
        var oldPath = CreateTempFile("old-restore-fail.exe");
        var repairedPath = CreateTempFile("new-restore-fail.exe");
        var app = new AppEntry
        {
            Id = "app1",
            Name = "App",
            ExePath = oldPath,
            AccountSid = "S-1-5-21-1",
            RestrictAcl = true
        };
        _session.Database.Apps.Add(app);
        _appConfigService.Setup(service => service.SaveConfigForApp(
                It.IsAny<string>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("save failed"));
        _besideTargetShortcutService.Setup(service => service.CreateBesideTargetShortcut(
                It.IsAny<AppEntry>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("shortcut restore failed"));

        var result = CreateCommitter().Commit(
            app,
            new VersionedPathRepairResult(
                repairedPath,
                Path.GetDirectoryName(oldPath)!,
                Path.GetDirectoryName(repairedPath)!,
                "app-1.0",
                "app-1.1"));

        Assert.False(result.Repaired);
        Assert.True(result.SaveFailed);
        Assert.Contains("save failed", result.WarningMessage, StringComparison.Ordinal);
        Assert.Contains("Enforcement restore failed: shortcut restore failed", result.WarningMessage, StringComparison.Ordinal);
        _iconService.Verify(service => service.GetIconPath(It.Is<string>(id => id == "app1")), Times.Once);
        _besideTargetShortcutService.Verify(service => service.CreateBesideTargetShortcut(
            It.Is<AppEntry>(candidate => candidate.ExePath == oldPath),
            _launcherPathProvider.GetLauncherPath(),
            "stored.ico",
            "Alice"), Times.Once);
        _iconService.Verify(service => service.CreateBadgedIcon(It.Is<AppEntry>(candidate => candidate.ExePath == oldPath), It.IsAny<string?>()), Times.Never);
    }

    private AppEntryPathRepairCommitter CreateCommitter()
    {
        var enforcementCoordinator = AppEntryEnforcementTestFactory.CreateCoordinator(
            _aclService.Object,
            _shortcutService.Object,
            _besideTargetShortcutService.Object,
            _iconService.Object,
            _sidNameCache.Object,
            _desktopProvider.Object,
            _interactiveUserSidResolver.Object,
            _launcherPathProvider,
            _log.Object);

        return new AppEntryPathRepairCommitter(
            new LambdaSessionProvider(() => _session),
            _appConfigService.Object,
            _iconService.Object,
            enforcementCoordinator,
            _aclService.Object);
    }

    private static string CreateTempFile(string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RunFenceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "test");
        File.SetLastWriteTimeUtc(path, new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        return path;
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}
