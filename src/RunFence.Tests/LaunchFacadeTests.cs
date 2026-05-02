using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class LaunchFacadeTests : IDisposable
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";

    private readonly Mock<IProcessLauncher> _processLauncher = new();
    private readonly Mock<ILaunchDefaultsResolver> _defaultsResolver = new();
    private readonly Mock<ILaunchTargetResolver> _launchTargetResolver = new();
    private readonly Mock<ILaunchAccessManager> _launchAccessManager = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly Mock<IProfilePathResolver> _profilePathResolver = new();
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker = new();
    private readonly LaunchFacade _facade;
    private readonly ProtectedBuffer _protectedPinKey;
    private readonly AppDatabase _database;

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
        _protectedPinKey = new ProtectedBuffer(new byte[32], protect: false);
        _database = new AppDatabase();

        _sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore(),
            PinDerivedKey = _protectedPinKey
        });

        _defaultsResolver
            .Setup(a => a.ResolveDefaults(It.IsAny<LaunchIdentity>()))
            .Returns<LaunchIdentity>(id => id);

        _launchTargetResolver
            .Setup(r => r.TraversePath(It.IsAny<string>(), It.IsAny<LaunchIdentity>()))
            .Returns<string, LaunchIdentity>((path, _) => new TraversePathResult(path, null, null, false));
        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(It.IsAny<LaunchIdentity>(), It.IsAny<ProcessLaunchTarget>()))
            .Returns<LaunchIdentity, ProcessLaunchTarget>((_, target) => WrapForResolution(target));
        _launchTargetResolver
            .Setup(r => r.ResolveUrlHandler(It.IsAny<LaunchIdentity>(), It.IsAny<string>()))
            .Returns<LaunchIdentity, string>((_, url) =>
                new LaunchTargetResolutionResult(ProcessLaunchHelper.BuildUrlLaunchTarget(url), LaunchResolutionKind.ShellWrapped, null));

        _processLauncher
            .Setup(p => p.Launch(It.IsAny<LaunchIdentity>(), It.IsAny<ProcessLaunchTarget>()))
            .Returns(MakeProcessInfo);

        _uiThreadInvoker.Setup(u => u.BeginInvoke(It.IsAny<Action>())).Callback<Action>(a => a());

        _facade = new LaunchFacade(
            _processLauncher.Object,
            _defaultsResolver.Object,
            _launchTargetResolver.Object,
            _launchAccessManager.Object,
            _databaseService.Object,
            _sessionProvider.Object,
            _profilePathResolver.Object,
            _uiThreadInvoker.Object);
    }

    public void Dispose() => _protectedPinKey.Dispose();

    [Fact]
    public void LaunchFile_ExeTarget_ReturnsProcessInfo()
    {
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);
        var expected = MakeProcessInfo();
        _processLauncher
            .Setup(p => p.Launch(identity, It.IsAny<ProcessLaunchTarget>()))
            .Returns(expected);

        var result = _facade.LaunchFile(target, identity);

        Assert.Same(expected, result);
        _launchTargetResolver.Verify(r => r.TraversePath(target.ExePath, identity), Times.Once);
        _launchTargetResolver.Verify(r => r.ResolveFileHandler(identity, target), Times.Once);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == @"C:\apps\myapp.exe")), Times.Once);
    }

    [Fact]
    public void LaunchFile_BatTarget_WrapsTargetToCmdExe_ReturnsProcessInfo()
    {
        var target = new ProcessLaunchTarget(@"C:\scripts\setup.bat");
        var identity = new AccountLaunchIdentity(TestSid);

        var result = _facade.LaunchFile(target, identity);

        Assert.NotNull(result);
        _launchTargetResolver.Verify(r => r.TraversePath(target.ExePath, identity), Times.Once);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == "cmd.exe"
                && t.Arguments!.Contains("/c") && t.Arguments.Contains("setup.bat"))),
            Times.Once);
    }

    [Fact]
    public void LaunchFile_NonExeNonScriptTarget_WrapsAndDisposesResult_ReturnsNull()
    {
        var target = new ProcessLaunchTarget(@"C:\installers\app.msi");
        var identity = new AccountLaunchIdentity(TestSid);

        var result = _facade.LaunchFile(target, identity);

        Assert.Null(result);
        _launchTargetResolver.Verify(r => r.TraversePath(target.ExePath, identity), Times.Once);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == "rundll32.exe"
                && t.Arguments!.Contains("shell32.dll,ShellExec_RunDLL"))), Times.Once);
    }

    [Fact]
    public void LaunchFile_FolderKindResult_RedirectsToLaunchFolderBrowser()
    {
        var folderPath = @"C:\Users\User\Documents";
        var target = new ProcessLaunchTarget(folderPath);
        var identity = new AccountLaunchIdentity(TestSid);

        _database.Settings.FolderBrowserExePath = @"C:\tools\explorer.exe";
        _database.Settings.FolderBrowserArguments = "%1";

        _launchTargetResolver
            .Setup(r => r.TraversePath(folderPath, identity))
            .Returns(new TraversePathResult(folderPath, null, null, true));

        _facade.LaunchFile(target, identity);

        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == @"C:\tools\explorer.exe"
                && t.Arguments == folderPath)), Times.Once);
    }

    [Fact]
    public void LaunchFile_ResolvedAssociationTarget_ReturnsDirectProcessInfo()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\docs\report.pdf");
        var resolvedTarget = new ProcessLaunchTarget(
            @"C:\Program Files\Viewer\viewer.exe",
            @"""C:\docs\report.pdf""",
            @"C:\docs");
        var identity = new AccountLaunchIdentity(TestSid);
        var expected = MakeProcessInfo();

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(identity, originalTarget))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.Handler, null));
        _processLauncher
            .Setup(p => p.Launch(identity, resolvedTarget))
            .Returns(expected);

        var result = _facade.LaunchFile(originalTarget, identity);

        Assert.Same(expected, result);
        _processLauncher.Verify(p => p.Launch(identity, resolvedTarget), Times.Once);
    }

    [Fact]
    public void LaunchFile_ResolvedAssociationTarget_WithLegacyEquivalentValues_StillReturnsDirectProcessInfo()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\docs\report.pdf");
        var legacyEquivalentResolvedTarget = new ProcessLaunchTarget(
            "rundll32.exe",
            @"shell32.dll,ShellExec_RunDLL C:\docs\report.pdf",
            @"C:\docs");
        var identity = new AccountLaunchIdentity(TestSid);
        var expected = MakeProcessInfo();

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(identity, originalTarget))
            .Returns(new LaunchTargetResolutionResult(legacyEquivalentResolvedTarget, LaunchResolutionKind.Handler, null));
        _processLauncher
            .Setup(p => p.Launch(identity, legacyEquivalentResolvedTarget))
            .Returns(expected);

        var result = _facade.LaunchFile(originalTarget, identity);

        Assert.Same(expected, result);
        _processLauncher.Verify(p => p.Launch(identity, legacyEquivalentResolvedTarget), Times.Once);
    }

    [Fact]
    public void LaunchFile_ResolvedAssociationTarget_UsesOriginalPathForPermissionGrant()
    {
        var originalTarget = new ProcessLaunchTarget(@"C:\docs\report.pdf", WorkingDirectory: @"C:\work");
        var resolvedTarget = new ProcessLaunchTarget(
            @"C:\Program Files\Viewer\viewer.exe",
            @"""C:\docs\report.pdf""",
            @"C:\docs");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(identity, originalTarget))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.Handler, null));
        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        _facade.LaunchFile(originalTarget, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\docs", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\Program Files\Viewer", FileSystemRights.ReadAndExecute, prompt, true), Times.Never);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\work", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }

    [Fact]
    public void LaunchFile_ResolverHiveLease_DisposedAfterLaunchAttempt()
    {
        var target = new ProcessLaunchTarget(@"C:\docs\report.pdf");
        var resolvedTarget = new ProcessLaunchTarget(@"C:\Program Files\Viewer\viewer.exe", @"""C:\docs\report.pdf""");
        var identity = new AccountLaunchIdentity(TestSid);
        var lease = new TrackingDisposable();

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(identity, target))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.Handler, lease));

        _facade.LaunchFile(target, identity);

        Assert.True(lease.WaitUntilDisposed());
    }

    [Fact]
    public void LaunchFile_WithPrompt_CallsEnsureAccessOnExeDir()
    {
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        _facade.LaunchFile(target, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\apps", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }

    [Fact]
    public void LaunchFile_WithPrompt_DifferentWorkingDir_CallsEnsureAccessOnBoth()
    {
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe", (string?)null, @"C:\work");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        _facade.LaunchFile(target, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\apps", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\work", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }

    [Fact]
    public void LaunchFile_WithoutPrompt_GrantsAppDirectory()
    {
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);

        _facade.LaunchFile(target, identity, permissionPrompt: null);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, AppContext.BaseDirectory, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
    }

    [Fact]
    public void LaunchFile_BasicIdentity_WithoutPrompt_GrantsAppDirectory()
    {
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = PrivilegeLevel.Basic };

        _facade.LaunchFile(target, identity, permissionPrompt: null);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, AppContext.BaseDirectory, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
    }

    [Fact]
    public void LaunchFile_OwnAppDirectoryTarget_GrantsAppDirectoryAccess()
    {
        var ownExePath = Path.Combine(AppContext.BaseDirectory, "RunFence.exe");
        var target = new ProcessLaunchTarget(ownExePath);
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = PrivilegeLevel.HighestAllowed };

        _facade.LaunchFile(target, identity, permissionPrompt: null);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, AppContext.BaseDirectory, FileSystemRights.ReadAndExecute,
            null, false), Times.Once);
    }

    [Fact]
    public void LaunchFile_BasicIdentity_OwnSubdirectoryTarget_GrantsAppDirectoryAndTargetDirectory()
    {
        var ownSubDir = Path.Combine(AppContext.BaseDirectory, "tools");
        var target = new ProcessLaunchTarget(Path.Combine(ownSubDir, "helper.exe"));
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = PrivilegeLevel.Basic };

        _facade.LaunchFile(target, identity, permissionPrompt: null);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, ownSubDir, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, AppContext.BaseDirectory, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
    }

    [Fact]
    public void LaunchFile_ContainerIdentity_WithPrompt_GrantsViaLaunchAccessManager()
    {
        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = ContainerSid };
        var identity = new AppContainerLaunchIdentity(entry);
        var target = new ProcessLaunchTarget(@"C:\apps\browser.exe");
        Func<string, string, bool> prompt = (_, _) => true;

        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        _facade.LaunchFile(target, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\apps", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }

    [Fact]
    public void LaunchFile_ContainerIdentity_NonExeLegacyWrappedTarget_ReturnsNull()
    {
        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = ContainerSid };
        var identity = new AppContainerLaunchIdentity(entry);
        var target = new ProcessLaunchTarget(@"C:\docs\report.pdf");

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(identity, target))
            .Returns(() =>
            {
                return new LaunchTargetResolutionResult(ProcessLaunchHelper.WrapForShellLaunch(target), LaunchResolutionKind.ShellWrapped, null);
            });

        var result = _facade.LaunchFile(target, identity);

        Assert.Null(result);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == "rundll32.exe"
                && t.Arguments == @"shell32.dll,ShellExec_RunDLL C:\docs\report.pdf")),
            Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_ExeBrowserExe_ReturnsProcessInfo()
    {
        _database.Settings.FolderBrowserExePath = @"C:\tools\explorer.exe";
        _database.Settings.FolderBrowserArguments = "%1";

        var identity = new AccountLaunchIdentity(TestSid);
        var folderPath = @"C:\Users\User\Documents";

        var result = _facade.LaunchFolderBrowser(identity, folderPath);

        Assert.NotNull(result);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == @"C:\tools\explorer.exe")), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_CmdBrowserExe_WrapsCorrectly()
    {
        _database.Settings.FolderBrowserExePath = @"C:\tools\browse.cmd";
        _database.Settings.FolderBrowserArguments = "%1";

        var identity = new AccountLaunchIdentity(TestSid);
        var folderPath = @"C:\Users\User\Documents";

        var result = _facade.LaunchFolderBrowser(identity, folderPath);

        Assert.NotNull(result);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == "cmd.exe")), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_ResolvedAssociationTarget_UsesResolverAndOriginalBrowserPathForGrant()
    {
        _database.Settings.FolderBrowserExePath = @"C:\tools\browser.pdf";
        _database.Settings.FolderBrowserArguments = "%1";

        var identity = new AccountLaunchIdentity(TestSid);
        var folderPath = @"C:\Users\User\Documents";
        var resolvedTarget = new ProcessLaunchTarget(
            @"C:\Program Files\Viewer\viewer.exe",
            @"""C:\Users\User\Documents""",
            folderPath);

        _launchTargetResolver
            .Setup(r => r.ResolveFileHandler(identity, It.Is<ProcessLaunchTarget>(t =>
                t.ExePath == @"C:\tools\browser.pdf"
                && t.Arguments == folderPath
                && t.WorkingDirectory == folderPath)))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.Handler, null));
        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        var result = _facade.LaunchFolderBrowser(identity, folderPath);

        Assert.NotNull(result);
        _processLauncher.Verify(p => p.Launch(identity, resolvedTarget), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\tools", FileSystemRights.ReadAndExecute, null, true), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\Program Files\Viewer", FileSystemRights.ReadAndExecute, null, true), Times.Never);
    }

    [Fact]
    public void LaunchFolderBrowser_WithPrompt_CallsEnsureAccessOnFolderPath()
    {
        _database.Settings.FolderBrowserExePath = @"C:\tools\explorer.exe";
        _database.Settings.FolderBrowserArguments = "%1";

        var identity = new AccountLaunchIdentity(TestSid);
        var folderPath = @"C:\Users\User\Documents";
        Func<string, string, bool> prompt = (_, _) => true;

        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        _facade.LaunchFolderBrowser(identity, folderPath, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, folderPath, FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_ContainerIdentity_WithPrompt_SkipsEnsureAccess()
    {
        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = ContainerSid };
        var identity = new AppContainerLaunchIdentity(entry);

        _database.Settings.FolderBrowserExePath = @"C:\tools\explorer.exe";
        _database.Settings.FolderBrowserArguments = "%1";

        var folderPath = @"C:\Users\User\Documents";

        _facade.LaunchFolderBrowser(identity, folderPath, (_, _) => true);

        _launchAccessManager.Verify(
            m => m.EnsureAccess(identity, folderPath, FileSystemRights.ReadAndExecute,
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public void LaunchUrl_DisposesResult()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        var url = "steam://run/12345";

        _facade.LaunchUrl(url, identity);

        _launchTargetResolver.Verify(r => r.ResolveUrlHandler(identity, url), Times.Once);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == "rundll32.exe"
                && t.Arguments!.Contains("url.dll,FileProtocolHandler"))),
            Times.Once);
    }

    [Fact]
    public void LaunchUrl_ResolvedHandler_GrantsAppDirectory()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        var resolvedTarget = new ProcessLaunchTarget(
            @"C:\Program Files\Browser\browser.exe",
            "\"steam://run/12345\"");
        _launchTargetResolver
            .Setup(r => r.ResolveUrlHandler(identity, "steam://run/12345"))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.Handler, null));

        _facade.LaunchUrl("steam://run/12345", identity);

        _processLauncher.Verify(p => p.Launch(identity, resolvedTarget), Times.Once);
        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, AppContext.BaseDirectory, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
    }

    [Fact]
    public void LaunchUrl_ResolverHiveLease_DisposedAfterLaunchAttempt()
    {
        var identity = new AccountLaunchIdentity(TestSid);
        var resolvedTarget = new ProcessLaunchTarget(
            @"C:\Program Files\Browser\browser.exe",
            "\"steam://run/12345\"");
        var lease = new TrackingDisposable();

        _launchTargetResolver
            .Setup(r => r.ResolveUrlHandler(identity, "steam://run/12345"))
            .Returns(new LaunchTargetResolutionResult(resolvedTarget, LaunchResolutionKind.Handler, lease));

        _facade.LaunchUrl("steam://run/12345", identity);

        Assert.True(lease.WaitUntilDisposed());
    }

    [Fact]
    public void LaunchFile_EnsureAccessDatabaseModified_TriggersSaveConfigAsync()
    {
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);

        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(true, false, true));

        _facade.LaunchFile(target, identity, (Func<string, string, bool>)((_, _) => true));

        _uiThreadInvoker.Verify(u => u.BeginInvoke(It.IsAny<Action>()), Times.AtLeastOnce);
    }

    [Theory]
    [InlineData(PrivilegeLevel.HighestAllowed, false)]
    [InlineData(PrivilegeLevel.Basic, true)]
    [InlineData(PrivilegeLevel.LowIntegrity, true)]
    public void LaunchFile_PrivilegeLevel_PassesCorrectUnelevated(PrivilegeLevel mode, bool expectedUnelevated)
    {
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = mode };
        Func<string, string, bool> prompt = (_, _) => true;

        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        _facade.LaunchFile(target, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\apps", FileSystemRights.ReadAndExecute, prompt, expectedUnelevated), Times.Once);
    }

    [Fact]
    public void LaunchFile_NullPrivilegeLevel_AccountDefaultHighestAllowed_PassesUnelevatedFalse()
    {
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _defaultsResolver
            .Setup(a => a.ResolveDefaults(identity))
            .Returns(identity with { PrivilegeLevel = PrivilegeLevel.HighestAllowed });

        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        _facade.LaunchFile(target, identity, prompt);

        var resolvedIdentity = identity with { PrivilegeLevel = PrivilegeLevel.HighestAllowed };
        _launchAccessManager.Verify(m => m.EnsureAccess(
            resolvedIdentity, @"C:\apps", FileSystemRights.ReadAndExecute, prompt, false), Times.Once);
    }

    [Fact]
    public void LaunchFile_EnsureAccessThrowsOperationCanceled_PropagatesWithoutLaunch()
    {
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => false;

        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Throws(new OperationCanceledException("User declined."));

        Assert.Throws<OperationCanceledException>(() => _facade.LaunchFile(target, identity, prompt));
        _processLauncher.Verify(p => p.Launch(It.IsAny<LaunchIdentity>(),
            It.IsAny<ProcessLaunchTarget>()), Times.Never);
    }

    [Fact]
    public void LaunchFile_EnsureAccessThrowsInvalidOperation_PropagatesWithoutLaunch()
    {
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Throws(new InvalidOperationException("Deny conflict; null confirm."));

        Assert.Throws<InvalidOperationException>(() => _facade.LaunchFile(target, identity, prompt));
        _processLauncher.Verify(p => p.Launch(It.IsAny<LaunchIdentity>(),
            It.IsAny<ProcessLaunchTarget>()), Times.Never);
    }

    [Fact]
    public void LaunchFolderBrowser_EnsureAccessThrowsOperationCanceled_PropagatesWithoutLaunch()
    {
        _database.Settings.FolderBrowserExePath = @"C:\tools\explorer.exe";
        _database.Settings.FolderBrowserArguments = "%1";

        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => false;

        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Throws(new OperationCanceledException("Folder access declined."));

        Assert.Throws<OperationCanceledException>(
            () => _facade.LaunchFolderBrowser(identity, @"C:\SomeFolder", prompt));
        _processLauncher.Verify(p => p.Launch(It.IsAny<LaunchIdentity>(),
            It.IsAny<ProcessLaunchTarget>()), Times.Never);
    }

    [Fact]
    public void LaunchFile_LowIntegrityIdentity_WithPrompt_DelegatesToLaunchAccessManager()
    {
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = PrivilegeLevel.LowIntegrity };
        Func<string, string, bool> prompt = (_, _) => true;

        _launchAccessManager
            .Setup(m => m.EnsureAccess(It.IsAny<LaunchIdentity>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        _facade.LaunchFile(target, identity, prompt);

        _launchAccessManager.Verify(m => m.EnsureAccess(
            identity, @"C:\apps", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }
}
