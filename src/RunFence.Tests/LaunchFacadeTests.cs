using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class LaunchFacadeTests : IDisposable
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string ContainerRootPath = @"D:\AppContainers";

    private readonly Mock<IProcessLauncher> _processLauncher = new();
    private readonly Mock<ILaunchDefaultsResolver> _defaultsResolver = new();
    private readonly Mock<ILaunchTargetResolver> _launchTargetResolver = new();
    private readonly Mock<ILaunchAccessManager> _launchAccessManager = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly Mock<IProfilePathResolver> _profilePathResolver = new();
    private readonly Mock<IFolderHandlerService> _folderHandlerService = new();
    private readonly Mock<IAssociationAutoSetService> _associationAutoSetService = new();
    private readonly LaunchFacade _facade;
    private readonly SecureSecret _protectedPinKey;
    private readonly AppDatabase _database;
    private readonly IUiThreadInvoker _uiThreadInvoker = new InlineUiThreadInvoker(action => action());

    private static ProcessInfo MakeProcessInfo()
        => new(new ProcessLaunchNative.PROCESS_INFORMATION());

    private static LaunchTargetResolutionResult WrapForResolution(ProcessLaunchTarget target)
    {
        if (ProcessLaunchHelper.CanLaunchDirect(target))
            return new LaunchTargetResolutionResult(target, LaunchResolutionKind.Direct, null);
        var scriptWrapped = ProcessLaunchHelper.TryWrapForScriptLaunch(target);
        if (scriptWrapped != null)
            return new LaunchTargetResolutionResult(scriptWrapped, LaunchResolutionKind.Script, null);
        return new LaunchTargetResolutionResult(ProcessLaunchHelper.WrapForShellLaunch(target), LaunchResolutionKind.ShellWrapped, null);
    }

    public LaunchFacadeTests()
    {
        _protectedPinKey = TestSecretFactory.Create(32);
        _database = new AppDatabase();
        _database.Settings.FolderBrowserExePath = @"C:\tools\explorer.exe";
        _database.Settings.FolderBrowserArguments = "%1";

        _sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
            Database = _database,
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(_protectedPinKey));

        _defaultsResolver
            .Setup(a => a.ResolveDefaults(It.IsAny<LaunchIdentity>(), It.IsAny<AppDatabase>()))
            .Returns<LaunchIdentity, AppDatabase>((id, _) => id);

        _launchTargetResolver
            .Setup(r => r.TraversePath(It.IsAny<string>(), It.IsAny<LaunchIdentity>(), It.IsAny<AppDatabase>()))
            .Returns<string, LaunchIdentity, AppDatabase>((path, _, _) => new TraversePathResult(path, null, null, false, Path.GetExtension(path)));
        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(It.IsAny<LaunchIdentity>(), It.IsAny<ProcessLaunchTarget>(), It.IsAny<AppDatabase>(), It.IsAny<string?>()))
            .Returns<LaunchIdentity, ProcessLaunchTarget, AppDatabase, string?>((_, target, _, _) => WrapForResolution(target));
        _launchTargetResolver
            .Setup(r => r.ResolveUrlHandler(It.IsAny<LaunchIdentity>(), It.IsAny<string>(), It.IsAny<AppDatabase>()))
            .Returns<LaunchIdentity, string, AppDatabase>((_, url, _) =>
                new LaunchTargetResolutionResult(ProcessLaunchHelper.BuildUrlLaunchTarget(url), LaunchResolutionKind.ShellWrapped, null));

        _processLauncher
            .Setup(p => p.Launch(It.IsAny<LaunchIdentity>(), It.IsAny<ProcessLaunchTarget>()))
            .Returns(MakeProcessInfo);

        _launchAccessManager
            .Setup(m => m.EnsureAccess(
                It.IsAny<LaunchIdentity>(),
                It.IsAny<string>(),
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<string, string, bool>?>(),
                It.IsAny<bool>()))
            .Returns(new GrantApplyResult(GrantApplied: true, DurableSaveCompleted: true));

        _facade = new LaunchFacade(
            _processLauncher.Object,
            _defaultsResolver.Object,
            _launchTargetResolver.Object,
            _launchAccessManager.Object,
            Mock.Of<ILoggingService>(),
            new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => _database), () => _uiThreadInvoker),
            _profilePathResolver.Object,
            _folderHandlerService.Object,
            _associationAutoSetService.Object,
            AppContainerProviderTestDoubles.CreatePathProvider(ContainerRootPath));
    }

    public void Dispose() => _protectedPinKey.Dispose();

    [Fact]
    public async Task LaunchFolderBrowser_WorkerThread_CapturesDatabaseSnapshotOnUiThread()
    {
        using var uiInvoker = new DedicatedThreadUiInvoker();
        var session = new SessionContext
{
            Database = _database,
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(_protectedPinKey);
        var guardedSessionProvider = new Mock<ISessionProvider>();
        var sessionThreadId = 0;
        guardedSessionProvider.Setup(s => s.GetSession())
            .Callback(() => sessionThreadId = Environment.CurrentManagedThreadId)
            .Returns(session);

        AppDatabase? resolverDatabase = null;
        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(
                It.IsAny<LaunchIdentity>(),
                It.IsAny<ProcessLaunchTarget>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<string?>()))
            .Callback<LaunchIdentity, ProcessLaunchTarget, AppDatabase, string?>((_, _, database, _) => resolverDatabase = database)
            .Returns<LaunchIdentity, ProcessLaunchTarget, AppDatabase, string?>((_, target, _, _) => WrapForResolution(target));

        var facade = new LaunchFacade(
            _processLauncher.Object,
            _defaultsResolver.Object,
            _launchTargetResolver.Object,
            _launchAccessManager.Object,
            Mock.Of<ILoggingService>(),
            new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() =>
                guardedSessionProvider.Object.GetSession().Database), () => uiInvoker),
            _profilePathResolver.Object,
            _folderHandlerService.Object,
            _associationAutoSetService.Object,
            AppContainerProviderTestDoubles.CreatePathProvider(ContainerRootPath));

        using var launch = await Task.Run(() => facade.LaunchFolderBrowser(new AccountLaunchIdentity(TestSid), @"C:\Users\User\Documents"));

        Assert.Equal(uiInvoker.ThreadId, sessionThreadId);
        Assert.NotNull(resolverDatabase);
        Assert.NotSame(_database, resolverDatabase);
    }

    [Fact]
    public void LaunchFile_UsesSingleSnapshotForDefaultsAndTargetResolution()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        var target = new ProcessLaunchTarget(@"C:\apps\tool.exe");
        AppDatabase? defaultsSnapshot = null;
        AppDatabase? resolverSnapshot = null;

        _defaultsResolver
            .Setup(a => a.ResolveDefaults(identity, It.IsAny<AppDatabase>()))
            .Callback<LaunchIdentity, AppDatabase>((_, database) => defaultsSnapshot = database)
            .Returns<LaunchIdentity, AppDatabase>((resolvedIdentity, _) => resolvedIdentity);
        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(identity, It.IsAny<ProcessLaunchTarget>(), It.IsAny<AppDatabase>(), ".exe"))
            .Callback<LaunchIdentity, ProcessLaunchTarget, AppDatabase, string?>((_, _, database, _) => resolverSnapshot = database)
            .Returns<LaunchIdentity, ProcessLaunchTarget, AppDatabase, string?>((_, resolvedTarget, _, _) => WrapForResolution(resolvedTarget));

        using var launch = _facade.LaunchFile(target, identity);

        Assert.NotNull(defaultsSnapshot);
        Assert.NotNull(resolverSnapshot);
        Assert.Same(defaultsSnapshot, resolverSnapshot);
        Assert.NotSame(_database, defaultsSnapshot);
    }

    [Fact]
    public void LaunchFile_WithPrompt_UsesPersistedGrantOnExeDir()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        using var launch = _facade.LaunchFile(new ProcessLaunchTarget(@"C:\apps\myapp.exe"), identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            @"C:\apps",
            FileSystemRights.ReadAndExecute,
            null,
            true), Times.Once);
    }

    [Fact]
    public void LaunchFile_ResolvedAssociationTarget_UsesOriginalPathForPermissionGrant()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\docs\report.pdf", WorkingDirectory: @"C:\work", IsPathApproved: false);
        var resolvedTarget = new ProcessLaunchTarget(
            @"C:\Program Files\Viewer\viewer.exe",
            @"""C:\docs\report.pdf""",
            @"C:\docs");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(
                identity,
                It.Is<ProcessLaunchTarget>(t =>
                    t.ExePath == originalTarget.ExePath &&
                    t.WorkingDirectory == originalTarget.WorkingDirectory &&
                    t.IsPathApproved == originalTarget.IsPathApproved),
                It.IsAny<AppDatabase>(),
                ".pdf"))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.Handler, null));

        using var launch = _facade.LaunchFile(originalTarget, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\docs", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\Program Files\Viewer", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\Program Files\Viewer", FileSystemRights.ReadAndExecute, null, true), Times.Never);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\work", FileSystemRights.ReadAndExecute, prompt, true), Times.Never);
    }

    [Fact]
    public void LaunchFile_ApprovedOriginalTarget_DoesNotApproveResolvedHandlerDirectory()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\docs\report.pdf", IsPathApproved: true);
        var resolvedTarget = new ProcessLaunchTarget(
            @"C:\Program Files\Viewer\viewer.exe",
            @"""C:\docs\report.pdf""",
            @"C:\docs");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(
                identity,
                It.Is<ProcessLaunchTarget>(t =>
                    t.ExePath == originalTarget.ExePath &&
                    t.IsPathApproved == originalTarget.IsPathApproved),
                It.IsAny<AppDatabase>(),
                ".pdf"))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.Handler, null));

        using var launch = _facade.LaunchFile(originalTarget, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\docs", FileSystemRights.ReadAndExecute, null, true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\docs", FileSystemRights.ReadAndExecute, prompt, true), Times.Never);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\Program Files\Viewer", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }

    [Fact]
    public void LaunchFile_ResolvedAssociationTarget_InOwnDirectory_UsesSilentGrantOnly()
    {
        var ownHandlerPath = Path.Combine(AppContext.BaseDirectory, "Viewer.exe");
        var originalTarget = new ProcessLaunchTarget(@"C:\docs\report.pdf", IsPathApproved: false);
        var resolvedTarget = new ProcessLaunchTarget(ownHandlerPath, @"""C:\docs\report.pdf""");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(
                identity,
                It.Is<ProcessLaunchTarget>(t =>
                    t.ExePath == originalTarget.ExePath &&
                    t.IsPathApproved == originalTarget.IsPathApproved),
                It.IsAny<AppDatabase>(),
                ".pdf"))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.Handler, null));

        using var launch = _facade.LaunchFile(originalTarget, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            PathHelper.NormalizeComparablePath(AppContext.BaseDirectory),
            FileSystemRights.ReadAndExecute,
            null,
            true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            @"C:\docs",
            FileSystemRights.ReadAndExecute,
            prompt,
            true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            PathHelper.NormalizeComparablePath(AppContext.BaseDirectory),
            FileSystemRights.ReadAndExecute,
            prompt,
            true), Times.Never);
    }

    [Fact]
    public void LaunchFile_ShellWrappedAssociation_DoesNotPromptForResolvedHandlerDirectory()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\docs\report.pdf", IsPathApproved: false);
        var resolvedTarget = ProcessLaunchHelper.WrapForShellLaunch(originalTarget);
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(
                identity,
                It.Is<ProcessLaunchTarget>(t =>
                    t.ExePath == originalTarget.ExePath &&
                    t.IsPathApproved == originalTarget.IsPathApproved),
                It.IsAny<AppDatabase>(),
                ".pdf"))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.ShellWrapped, null));

        using var launch = _facade.LaunchFile(originalTarget, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            @"C:\docs",
            FileSystemRights.ReadAndExecute,
            prompt,
            true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            Path.GetDirectoryName(resolvedTarget.ExePath)!,
            FileSystemRights.ReadAndExecute,
            prompt,
            true), Times.Never);
    }

    [Fact]
    public void LaunchFile_ShellWrappedApprovedTarget_KeepsOriginalTargetGrantSilent()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\docs\report.pdf", IsPathApproved: true);
        var resolvedTarget = ProcessLaunchHelper.WrapForShellLaunch(originalTarget);
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(
                identity,
                It.Is<ProcessLaunchTarget>(t =>
                    t.ExePath == originalTarget.ExePath &&
                    t.IsPathApproved == originalTarget.IsPathApproved),
                It.IsAny<AppDatabase>(),
                ".pdf"))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.ShellWrapped, null));

        using var launch = _facade.LaunchFile(originalTarget, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            @"C:\docs",
            FileSystemRights.ReadAndExecute,
            null,
            true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            @"C:\docs",
            FileSystemRights.ReadAndExecute,
            prompt,
            true), Times.Never);
    }

    [Fact]
    public void LaunchFile_ScriptWrappedApprovedTarget_KeepsOriginalTargetGrantSilent()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\scripts\deploy.ps1", IsPathApproved: true);
        var resolvedTarget = ProcessLaunchHelper.TryWrapForScriptLaunch(originalTarget)!;
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(
                identity,
                It.Is<ProcessLaunchTarget>(t =>
                    t.ExePath == originalTarget.ExePath &&
                    t.IsPathApproved == originalTarget.IsPathApproved),
                It.IsAny<AppDatabase>(),
                ".ps1"))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.Script, null));

        using var launch = _facade.LaunchFile(originalTarget, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            @"C:\scripts",
            FileSystemRights.ReadAndExecute,
            null,
            true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            @"C:\scripts",
            FileSystemRights.ReadAndExecute,
            prompt,
            true), Times.Never);
    }

    [Fact]
    public void LaunchFile_ResolvedAssociationTarget_PreservesResolvedArguments()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\docs\report.pdf");
        var resolvedTarget = new ProcessLaunchTarget(
            @"C:\Program Files\Viewer\viewer.exe",
            @"""C:\docs\report.pdf"" --open");
        var identity = new AccountLaunchIdentity(TestSid);

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(
                identity,
                It.Is<ProcessLaunchTarget>(t =>
                    t.ExePath == originalTarget.ExePath &&
                    t.IsPathApproved == originalTarget.IsPathApproved),
                It.IsAny<AppDatabase>(),
                ".pdf"))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.Handler, null));

        using var launch = _facade.LaunchFile(originalTarget, identity, (_, _) => true);

        _processLauncher.Verify(p => p.Launch(
            identity,
            It.Is<ProcessLaunchTarget>(t =>
                t.ExePath == resolvedTarget.ExePath &&
                t.Arguments == resolvedTarget.Arguments)),
            Times.Once);
    }

    [Fact]
    public void LaunchFile_ForwardsExtensionFromTraversal()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\links\report-link.exe");
        var identity = new AccountLaunchIdentity(TestSid);
        var traversed = new TraversePathResult(
            @"C:\targets\report.pdf",
            null,
            null,
            false,
            ".exe");

        _launchTargetResolver
            .Setup(r => r.TraversePath(originalTarget.ExePath, identity, It.IsAny<AppDatabase>()))
            .Returns(traversed);

        using var launch = _facade.LaunchFile(originalTarget, identity);

        _launchTargetResolver.Verify(r => r.ResolveFileHandler(
            identity,
            It.Is<ProcessLaunchTarget>(t =>
                t.ExePath == traversed.TraversedPath &&
                t.IsPathApproved == false),
            It.IsAny<AppDatabase>(),
            traversed.Extension),
            Times.Once);
    }

    [Fact]
    public void LaunchFile_GrantFailureBlocksLaunchAndPropagates()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        var cause = new InvalidOperationException("save failed");
        _launchAccessManager
            .Setup(m => m.EnsureAccess(
                It.IsAny<LaunchIdentity>(),
                It.IsAny<string>(),
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<string, string, bool>?>(),
                It.IsAny<bool>()))
            .Throws(new GrantOperationException(GrantApplyFailureStep.GrantIntentSave, @"C:\apps", null, cause));

        var ex = Assert.Throws<GrantOperationException>(() =>
            _facade.LaunchFile(new ProcessLaunchTarget(@"C:\apps\myapp.exe"), identity, (_, _) => true));

        Assert.Same(cause, ex.Cause);
        _processLauncher.Verify(p => p.Launch(It.IsAny<LaunchIdentity>(), It.IsAny<ProcessLaunchTarget>()), Times.Never);
    }

    [Fact]
    public void LaunchFolderBrowser_WithPrompt_UsesPersistedGrantOnFolderPath()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        var folderPath = @"C:\Users\User\Documents";
        Func<string, string, bool> prompt = (_, _) => true;

        using var launch = _facade.LaunchFolderBrowser(identity, folderPath, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, folderPath, FileSystemRights.ReadAndExecute, null, true), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_UnapprovedTarget_UsesPromptForFolderPath()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        var folderPath = @"C:\Users\User\Documents";
        Func<string, string, bool> prompt = (_, _) => true;

        using var launch = _facade.LaunchFolderBrowser(identity, folderPath, prompt, isTargetApproved: false);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, folderPath, FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_ContainerIdentity_WithPrompt_UsesFolderPathGrant()
    {
        var identity = new AppContainerLaunchIdentity(new AppContainerEntry { Name = "ram_browser", Sid = ContainerSid });
        var folderPath = @"C:\Users\User\Documents";

        using var launch = _facade.LaunchFolderBrowser(identity, folderPath, (_, _) => true);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            folderPath,
            FileSystemRights.ReadAndExecute,
            null,
            true), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_ContainerIdentityWithoutFolderPath_UsesAppContainerPathProvider()
    {
        var identity = new AppContainerLaunchIdentity(new AppContainerEntry { Name = "ram_browser", Sid = ContainerSid });
        var expectedPath = Path.Combine(ContainerRootPath, "ram_browser");

        using var launch = _facade.LaunchFolderBrowser(identity);

        _processLauncher.Verify(p => p.Launch(
            identity,
            It.Is<ProcessLaunchTarget>(t =>
                t.ExePath == @"C:\tools\explorer.exe" &&
                t.Arguments == expectedPath &&
                t.WorkingDirectory == expectedPath)),
            Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_DriveRoot_PreservesRootForGrantAndWorkingDirectory()
    {
        _database.Settings.FolderBrowserArguments = "\"%1\"";
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        using var launch = _facade.LaunchFolderBrowser(identity, @"C:\", prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:", FileSystemRights.ReadAndExecute, null, true), Times.Once);
        _processLauncher.Verify(p => p.Launch(
            identity,
            It.Is<ProcessLaunchTarget>(t =>
                t.WorkingDirectory == @"C:\" &&
                t.Arguments == "\"C:\\\\\"")),
            Times.Once);
    }

    [Fact]
    public void LaunchFile_FolderTraversal_ResetsApprovalBeforeFolderBrowser()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        var originalTarget = new ProcessLaunchTarget(@"C:\docs\folder-link.lnk", IsPathApproved: true);
        Func<string, string, bool> prompt = (_, _) => true;

        _launchTargetResolver
            .Setup(r => r.TraversePath(originalTarget.ExePath, identity, It.IsAny<AppDatabase>()))
            .Returns(new TraversePathResult(@"C:\docs\ActualFolder", null, null, true));

        using var launch = _facade.LaunchFile(originalTarget, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            @"C:\docs\ActualFolder",
            FileSystemRights.ReadAndExecute,
            prompt,
            true), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_UsesCommandLineAwarePlaceholderSubstitution()
    {
        _database.Settings.FolderBrowserArguments = "--folder \"%1\"";
        var identity = new AccountLaunchIdentity(TestSid);

        using var launch = _facade.LaunchFolderBrowser(identity, @"C:\", (_, _) => true);

        _processLauncher.Verify(p => p.Launch(
            identity,
            It.Is<ProcessLaunchTarget>(t => t.Arguments == "--folder \"C:\\\\\"")),
            Times.Once);
    }

    [Fact]
    public void LaunchUrl_GrantsRunFenceBaseDirectoryBeforeLaunch()
    {
        var identity = new AccountLaunchIdentity(TestSid);

        using var launch = _facade.LaunchUrl("steam://run/12345", identity);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity,
            PathHelper.NormalizeComparablePath(AppContext.BaseDirectory),
            FileSystemRights.ReadAndExecute,
            null,
            true), Times.Once);
    }

    [Fact]
    public void LaunchUrl_AssociationResolutionFailure_PropagatesTypedException()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        _launchTargetResolver
            .Setup(r => r.ResolveUrlHandler(identity, "ms-settings:defaultapps", It.IsAny<AppDatabase>()))
            .Throws(new AssociationResolutionException("No usable association handler found for 'ms-settings:defaultapps'."));

        var ex = Assert.Throws<AssociationResolutionException>(() =>
            _facade.LaunchUrl("ms-settings:defaultapps", identity));

        Assert.Equal("No usable association handler found for 'ms-settings:defaultapps'.", ex.Message);
        _processLauncher.Verify(p => p.Launch(It.IsAny<LaunchIdentity>(), It.IsAny<ProcessLaunchTarget>()), Times.Never);
    }

    [Fact]
    public void LaunchFile_NonShellWrappedLaunchWithoutProcess_Throws()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        _processLauncher
            .Setup(p => p.Launch(It.IsAny<LaunchIdentity>(), It.IsAny<ProcessLaunchTarget>()))
            .Returns((ProcessInfo?)null);

        var ex = Assert.Throws<InvalidOperationException>(() => _facade.LaunchFile(target, identity));

        Assert.Contains("did not return a process", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchUrl_ShellWrappedLaunchWithoutProcess_ReturnsShellWrappedNoProcess()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        _processLauncher
            .Setup(p => p.Launch(It.IsAny<LaunchIdentity>(), It.IsAny<ProcessLaunchTarget>()))
            .Returns((ProcessInfo?)null);

        using var launch = _facade.LaunchUrl("steam://run/12345", identity);

        Assert.Equal(LaunchExecutionStatus.ShellWrappedNoProcess, launch.Status);
        Assert.Null(launch.Process);
    }

    [Fact]
    public void LaunchFile_FolderHandlerFailure_ReturnsMaintenanceWarningAndKeepsProcess()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        var processInfo = MakeProcessInfo();
        _processLauncher.Setup(p => p.Launch(It.IsAny<LaunchIdentity>(), It.IsAny<ProcessLaunchTarget>()))
            .Returns(processInfo);
        _folderHandlerService.Setup(s => s.Register(TestSid)).Throws(new InvalidOperationException("folder handler failed"));

        using var launch = _facade.LaunchFile(new ProcessLaunchTarget(@"C:\apps\myapp.exe"), identity);

        Assert.Equal(LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings, launch.Status);
        Assert.Same(processInfo, launch.Process);
        Assert.Contains(launch.MaintenanceWarnings, warning => warning.Contains("folder handler failed", StringComparison.Ordinal));
        _associationAutoSetService.Verify(s => s.AutoSetForUser(TestSid), Times.Once);
    }

    [Fact]
    public void LaunchFile_FolderHandlerWarning_ReturnsMaintenanceWarningAndKeepsProcess()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        var processInfo = MakeProcessInfo();
        _processLauncher.Setup(p => p.Launch(It.IsAny<LaunchIdentity>(), It.IsAny<ProcessLaunchTarget>()))
            .Returns(processInfo);
        _folderHandlerService.Setup(s => s.Register(TestSid))
            .Returns(new FolderHandlerRegistrationResult(new[] { "maintenance warning from folder handler" }));

        using var launch = _facade.LaunchFile(new ProcessLaunchTarget(@"C:\apps\myapp.exe"), identity);

        Assert.Equal(LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings, launch.Status);
        Assert.Same(processInfo, launch.Process);
        Assert.Contains(launch.MaintenanceWarnings, warning => warning.Contains("maintenance warning from folder handler", StringComparison.Ordinal));
    }

    [Fact]
    public void LaunchExecutionWarningFormatter_FormatsOnlyMaintenanceWarnings()
    {
        using var launch = new LaunchExecutionResult(
            LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings,
            null,
            ["warning one", "warning two"]);

        var message = LaunchExecutionWarningFormatter.Format("The application", launch);

        Assert.NotNull(message);
        Assert.Contains("warning one", message, StringComparison.Ordinal);
        Assert.Contains("warning two", message, StringComparison.Ordinal);
    }
}
