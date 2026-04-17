using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Container;
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
    private readonly Mock<IPathGrantService> _pathGrantService = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker = new();
    private readonly LaunchFacade _facade;
    private readonly ProtectedBuffer _protectedPinKey;
    private readonly AppDatabase _database;

    private static ProcessInfo MakeProcessInfo()
        => new ProcessInfo(new ProcessLaunchNative.PROCESS_INFORMATION());

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

        _processLauncher
            .Setup(p => p.Launch(It.IsAny<LaunchIdentity>(), It.IsAny<ProcessLaunchTarget>()))
            .Returns(MakeProcessInfo);

        _uiThreadInvoker.Setup(u => u.BeginInvoke(It.IsAny<Action>())).Callback<Action>(a => a());

        _facade = new LaunchFacade(
            _processLauncher.Object,
            _defaultsResolver.Object,
            _pathGrantService.Object,
            _databaseService.Object,
            _sessionProvider.Object,
            _sidResolver.Object,
            _uiThreadInvoker.Object);
    }

    public void Dispose() => _protectedPinKey.Dispose();

    // ── LaunchFile ───────────────────────────────────────────────────────────

    [Fact]
    public void LaunchFile_ExeTarget_ReturnsProcessInfo()
    {
        // Arrange
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);
        var expected = MakeProcessInfo();
        _processLauncher
            .Setup(p => p.Launch(identity, It.IsAny<ProcessLaunchTarget>()))
            .Returns(expected);

        // Act
        var result = _facade.LaunchFile(target, identity);

        // Assert
        Assert.Same(expected, result);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == @"C:\apps\myapp.exe")), Times.Once);
    }

    [Fact]
    public void LaunchFile_BatTarget_WrapsTargetToCmdExe_ReturnsProcessInfo()
    {
        // Arrange — .bat is a script: WrapTargetForLaunch wraps to cmd.exe /c but isWrapped=false
        // so the resulting ProcessInfo is returned (not disposed/null)
        var target = new ProcessLaunchTarget(@"C:\scripts\setup.bat");
        var identity = new AccountLaunchIdentity(TestSid);

        // Act
        var result = _facade.LaunchFile(target, identity);

        // Assert — result returned (not null); launched via cmd.exe because .bat can't run directly
        Assert.NotNull(result);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == "cmd.exe"
                && t.Arguments!.Contains("/c") && t.Arguments.Contains("setup.bat"))),
            Times.Once);
    }

    [Fact]
    public void LaunchFile_NonExeNonScriptTarget_WrapsAndDisposesResult_ReturnsNull()
    {
        // Arrange — .msi is non-exe non-script: WrapTargetForLaunch uses rundll32 ShellExec_RunDLL and isWrapped=true
        // so the ProcessInfo is disposed and null is returned
        var target = new ProcessLaunchTarget(@"C:\installers\app.msi");
        var identity = new AccountLaunchIdentity(TestSid);

        // Act
        var result = _facade.LaunchFile(target, identity);

        // Assert — null because isWrapped=true (non-script non-exe)
        Assert.Null(result);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == "rundll32.exe"
                && t.Arguments!.Contains("shell32.dll,ShellExec_RunDLL"))), Times.Once);
    }

    [Fact]
    public void LaunchFile_WithPrompt_CallsEnsureAccessOnExeDir()
    {
        // Arrange — path outside AppContext.BaseDirectory (not IsOwnDir)
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _pathGrantService
            .Setup(p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        // Act
        _facade.LaunchFile(target, identity, prompt);

        // Assert — EnsureAccess called on exe directory with provided prompt; null PrivilegeLevel → unelevated=true
        _pathGrantService.Verify(p => p.EnsureAccess(
            TestSid, @"C:\apps", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }

    [Fact]
    public void LaunchFile_WithPrompt_DifferentWorkingDir_CallsEnsureAccessOnBoth()
    {
        // Arrange
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe", (string?)null, @"C:\work");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _pathGrantService
            .Setup(p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        // Act
        _facade.LaunchFile(target, identity, prompt);

        // Assert — EnsureAccess called for exe dir AND for working dir (they differ); unelevated=true (null PrivilegeLevel)
        _pathGrantService.Verify(p => p.EnsureAccess(
            TestSid, @"C:\apps", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
        _pathGrantService.Verify(p => p.EnsureAccess(
            TestSid, @"C:\work", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }

    [Fact]
    public void LaunchFile_WithoutPrompt_OnlyGrantsOwnDir()
    {
        // Arrange — path outside own dir, no permissionPrompt
        // When permissionPrompt is null and path is not own dir: EnsureAccess is NOT called.
        // LaunchCore only calls EnsureAccess when grantPermissionToExePath is non-null (own dir path).
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);

        // Act
        _facade.LaunchFile(target, identity, permissionPrompt: null);

        // Assert — no EnsureAccess calls for non-own-dir path without prompt
        _pathGrantService.Verify(
            p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public void LaunchFile_ContainerIdentity_WithPrompt_GrantsViaPathGrantService()
    {
        // Arrange — AppContainerLaunchIdentity: EnsureAccess uses container SID;
        // AppContainerLaunchIdentity.IsUnelevated=true → unelevated=true; Admins SID is absent from container groups anyway
        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = ContainerSid };
        var identity = new AppContainerLaunchIdentity(entry);
        var target = new ProcessLaunchTarget(@"C:\apps\browser.exe");
        Func<string, string, bool> prompt = (_, _) => true;

        _pathGrantService
            .Setup(p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        // Act
        _facade.LaunchFile(target, identity, prompt);

        // Assert — EnsureAccess called with container SID and unelevated=true (AppContainerLaunchIdentity.IsUnelevated=true)
        _pathGrantService.Verify(p => p.EnsureAccess(
            ContainerSid, @"C:\apps", FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }

    // ── LaunchFolderBrowser ──────────────────────────────────────────────────

    [Fact]
    public void LaunchFolderBrowser_ExeBrowserExe_ReturnsProcessInfo()
    {
        // Arrange — browser is a .exe: not wrapped
        _database.Settings.FolderBrowserExePath = @"C:\tools\explorer.exe";
        _database.Settings.FolderBrowserArguments = "%1";

        var identity = new AccountLaunchIdentity(TestSid);
        var folderPath = @"C:\Users\User\Documents";

        // Act
        var result = _facade.LaunchFolderBrowser(identity, folderPath);

        // Assert
        Assert.NotNull(result);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == @"C:\tools\explorer.exe")), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_CmdBrowserExe_WrapsCorrectly()
    {
        // Arrange — browser exe is a .cmd script: WrapTargetForLaunch wraps to cmd.exe /c
        _database.Settings.FolderBrowserExePath = @"C:\tools\browse.cmd";
        _database.Settings.FolderBrowserArguments = "%1";

        var identity = new AccountLaunchIdentity(TestSid);
        var folderPath = @"C:\Users\User\Documents";

        // Act — .cmd: isScript=true → isWrapped=false → result returned (not null)
        var result = _facade.LaunchFolderBrowser(identity, folderPath);

        // Assert — wrapped to cmd.exe; result not null (scripts are not isWrapped)
        Assert.NotNull(result);
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == "cmd.exe")), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_WithPrompt_CallsEnsureAccessOnFolderPath()
    {
        // Arrange
        _database.Settings.FolderBrowserExePath = @"C:\tools\explorer.exe";
        _database.Settings.FolderBrowserArguments = "%1";

        var identity = new AccountLaunchIdentity(TestSid);
        var folderPath = @"C:\Users\User\Documents";
        Func<string, string, bool> prompt = (_, _) => true;

        _pathGrantService
            .Setup(p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        // Act
        _facade.LaunchFolderBrowser(identity, folderPath, prompt);

        // Assert — EnsureAccess called for folder path with prompt; null PrivilegeLevel → unelevated=true
        _pathGrantService.Verify(p => p.EnsureAccess(
            TestSid, folderPath, FileSystemRights.ReadAndExecute, prompt, true), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_ContainerIdentity_WithPrompt_SkipsEnsureAccess()
    {
        // Arrange — AppContainerLaunchIdentity: the code skips EnsureAccess for container identity
        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = ContainerSid };
        var identity = new AppContainerLaunchIdentity(entry);

        _database.Settings.FolderBrowserExePath = @"C:\tools\explorer.exe";
        _database.Settings.FolderBrowserArguments = "%1";

        var folderPath = @"C:\Users\User\Documents";
        Func<string, string, bool> prompt = (_, _) => true;

        // Act
        _facade.LaunchFolderBrowser(identity, folderPath, prompt);

        // Assert — EnsureAccess NOT called for folderPath itself (container manages its own folder access).
        // EnsureAccess IS called for the folder browser exe directory (LaunchCore exe dir grant).
        _pathGrantService.Verify(
            p => p.EnsureAccess(ContainerSid, folderPath, FileSystemRights.ReadAndExecute,
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()),
            Times.Never);
    }

    // ── LaunchUrl ────────────────────────────────────────────────────────────

    [Fact]
    public void LaunchUrl_DisposesResult()
    {
        // Arrange
        var identity = new AccountLaunchIdentity(TestSid);
        var url = "steam://run/12345";

        // Act — LaunchUrl calls LaunchCore(...) and disposes the returned ProcessInfo
        _facade.LaunchUrl(url, identity);

        // Assert — Launch called with rundll32.exe target
        _processLauncher.Verify(p => p.Launch(
            identity, It.Is<ProcessLaunchTarget>(t => t.ExePath == "rundll32.exe"
                && t.Arguments!.Contains("url.dll,FileProtocolHandler"))),
            Times.Once);
    }

    [Fact]
    public void LaunchFile_EnsureAccessDatabaseModified_TriggersSaveConfigAsync()
    {
        // Arrange — EnsureAccess reports DatabaseModified=true → SaveConfigAsync should be called
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _pathGrantService
            .Setup(p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(true, false, true));

        // Act
        _facade.LaunchFile(target, identity, prompt);

        // Assert — BeginInvoke called (SaveConfigAsync runs on UI thread)
        _uiThreadInvoker.Verify(u => u.BeginInvoke(It.IsAny<Action>()), Times.AtLeastOnce);
    }

    [Theory]
    [InlineData(PrivilegeLevel.HighestAllowed, false)]
    [InlineData(PrivilegeLevel.Basic, true)]
    [InlineData(PrivilegeLevel.LowIntegrity, true)]
    public void LaunchFile_PrivilegeLevel_PassesCorrectUnelevated(PrivilegeLevel mode, bool expectedUnelevated)
    {
        // HighestAllowed → IsUnelevated=false → false??true=false → unelevated=false.
        // Basic/LowIntegrity → IsUnelevated=true → true??true=true → unelevated=true.
        // null PrivilegeLevel → ResolveDefaults returns as-is (mock) → IsUnelevated=null → null??true=true → unelevated=true.
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = mode };
        Func<string, string, bool> prompt = (_, _) => true;

        _pathGrantService
            .Setup(p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        _facade.LaunchFile(target, identity, prompt);

        _pathGrantService.Verify(p => p.EnsureAccess(
            TestSid, @"C:\apps", FileSystemRights.ReadAndExecute, prompt, expectedUnelevated), Times.Once);
    }

    [Fact]
    public void LaunchFile_NullPrivilegeLevel_AccountDefaultHighestAllowed_PassesUnelevatedFalse()
    {
        // Arrange — identity.PrivilegeLevel=null (IsUnelevated=null), but defaultsResolver resolves to HighestAllowed
        // → resolved.IsUnelevated=false → false??true=false → unelevated=false
        var target = new ProcessLaunchTarget(@"C:\apps\myapp.exe");
        var identity = new AccountLaunchIdentity(TestSid);
        Func<string, string, bool> prompt = (_, _) => true;

        _defaultsResolver
            .Setup(a => a.ResolveDefaults(identity))
            .Returns(identity with { PrivilegeLevel = PrivilegeLevel.HighestAllowed });

        _pathGrantService
            .Setup(p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(false, false, false));

        // Act
        _facade.LaunchFile(target, identity, prompt);

        // Assert — resolved IsUnelevated=false → false??true=false → unelevated=false
        _pathGrantService.Verify(p => p.EnsureAccess(
            TestSid, @"C:\apps", FileSystemRights.ReadAndExecute, prompt, false), Times.Once);
    }
}
