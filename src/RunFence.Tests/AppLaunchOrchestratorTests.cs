using System.Security;
using Moq;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Persistence;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class AppLaunchOrchestratorTests : IDisposable
{
    private readonly Mock<IProcessLaunchService> _processLaunchService;
    private readonly Mock<ICredentialEncryptionService> _encryptionService;
    private readonly Mock<ISidResolver> _sidResolver;
    private readonly Mock<IPermissionGrantService> _permissionGrantService;
    private readonly Mock<IAccountLauncher> _accountLauncher;
    private readonly Mock<IAppContainerService> _appContainerService;
    private readonly Mock<IFolderHandlerService> _folderHandlerService;
    private readonly Mock<IDatabaseService> _databaseService;
    private readonly AppLaunchOrchestrator _orchestrator;
    private readonly AppDatabase _database;
    private readonly CredentialStore _credentialStore;
    private readonly byte[] _pinDerivedKey = new byte[32];
    private readonly ProtectedBuffer _protectedPinKey;

    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private static readonly string CurrentSid = SidResolutionHelper.GetCurrentUserSid();
    private readonly string _tempExePath;

    public AppLaunchOrchestratorTests()
    {
        _tempExePath = Path.GetTempFileName();
        _processLaunchService = new Mock<IProcessLaunchService>();
        _encryptionService = new Mock<ICredentialEncryptionService>();
        _databaseService = new Mock<IDatabaseService>();
        _sidResolver = new Mock<ISidResolver>();
        _permissionGrantService = new Mock<IPermissionGrantService>();
        _accountLauncher = new Mock<IAccountLauncher>();
        _appContainerService = new Mock<IAppContainerService>();
        _folderHandlerService = new Mock<IFolderHandlerService>();
        _orchestrator = new AppLaunchOrchestrator(
            _processLaunchService.Object, _encryptionService.Object,
            _sidResolver.Object, _permissionGrantService.Object, _accountLauncher.Object,
            _appContainerService.Object, _folderHandlerService.Object,
            _databaseService.Object);

        _database = new AppDatabase
        {
            SidNames =
            {
                [TestSid] = "User"
            }
        };
        _database.GetOrCreateAccount(TestSid).SplitTokenOptOut = true;
        _database.GetOrCreateAccount(CurrentSid).SplitTokenOptOut = true;
        _credentialStore = new CredentialStore();
        _protectedPinKey = new ProtectedBuffer(_pinDerivedKey, protect: false);

        _orchestrator.SetData(new SessionContext
        {
            Database = _database,
            CredentialStore = _credentialStore,
            PinDerivedKey = _protectedPinKey
        });
    }

    public void Dispose()
    {
        _protectedPinKey.Dispose();
        try
        {
            File.Delete(_tempExePath);
        }
        catch
        {
        }
    }

    private SecureString SetupDecryptableCredential(string sid = TestSid)
    {
        var encrypted = new byte[] { 1, 2, 3 };
        _credentialStore.Credentials.Add(new CredentialEntry
            { Id = Guid.NewGuid(), Sid = sid, EncryptedPassword = encrypted });
        var decrypted = new SecureString();
        decrypted.AppendChar('x');
        decrypted.MakeReadOnly();
        _encryptionService.Setup(e => e.Decrypt(encrypted, _pinDerivedKey)).Returns(decrypted);
        return decrypted;
    }

    [Fact]
    public void Launch_CredentialNotFound_ThrowsCredentialNotFoundException()
    {
        var app = new AppEntry { AccountSid = "S-1-5-21-0000000000-0000000000-0000000000-9999", ExePath = _tempExePath };

        var ex = Assert.Throws<CredentialNotFoundException>(() =>
            _orchestrator.Launch(app, null));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void Launch_MissingPassword_ThrowsMissingPasswordException()
    {
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Id = Guid.NewGuid(), Sid = TestSid
            // EncryptedPassword = [] (default) → MissingPassword
        });

        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath };

        var ex = Assert.Throws<MissingPasswordException>(() =>
            _orchestrator.Launch(app, null));

        Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Launch_CurrentAccount_LaunchesWithNullPassword()
    {
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Id = Guid.NewGuid(),
            Sid = CurrentSid
        });

        var app = new AppEntry { AccountSid = CurrentSid, ExePath = _tempExePath };

        _orchestrator.Launch(app, null);

        // Current account uses Environment.UserName directly
        _processLaunchService.Verify(p => p.Launch(
            app,
            new LaunchCredentials(null, "", Environment.UserName, LaunchTokenSource.CurrentProcess),
            null, null, _database.Settings, default), Times.Once);
    }

    [Fact]
    public void Launch_StoredCredential_DecryptsAndLaunches()
    {
        var decryptedPassword = SetupDecryptableCredential();
        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath };
        _orchestrator.Launch(app, "--flag");

        // TryResolveName returns null for fake SIDs, so fallback uses SidNames map
        _processLaunchService.Verify(p => p.Launch(
            app,
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            "--flag", null, _database.Settings, default), Times.Once);
    }

    [Fact]
    public void LaunchDiscoveredApp_FileNotFound_ThrowsFileNotFoundException()
    {
        var ex = Assert.Throws<FileNotFoundException>(() =>
            _orchestrator.LaunchDiscoveredApp(@"C:\nonexistent\app.exe", TestSid));

        Assert.Contains("app.exe", ex.Message);
    }

    [Fact]
    public void LaunchDiscoveredApp_CredentialNotFound_ThrowsCredentialNotFoundException()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var ex = Assert.Throws<CredentialNotFoundException>(() =>
                _orchestrator.LaunchDiscoveredApp(tempFile, "S-1-5-21-0000000000-0000000000-0000000000-9999"));

            Assert.Contains("not found", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LaunchDiscoveredApp_MissingPassword_ThrowsMissingPasswordException()
    {
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Id = Guid.NewGuid(), Sid = TestSid
            // EncryptedPassword = [] (default) → MissingPassword
        });

        // Use a temp file so the File.Exists check passes
        var tempFile = Path.GetTempFileName();
        try
        {
            var ex = Assert.Throws<MissingPasswordException>(() =>
                _orchestrator.LaunchDiscoveredApp(tempFile, TestSid));

            Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LaunchDiscoveredApp_Success_LaunchesExeWithCorrectParams()
    {
        var decryptedPassword = SetupDecryptableCredential();
        var tempFile = Path.GetTempFileName();
        try
        {
            _orchestrator.LaunchDiscoveredApp(tempFile, TestSid);

            _processLaunchService.Verify(p => p.LaunchExe(
                new ProcessLaunchTarget(tempFile, null, Path.GetDirectoryName(tempFile), null),
                new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
                default), Times.Once);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LaunchExe_CredentialNotFound_ThrowsCredentialNotFoundException()
    {
        var ex = Assert.Throws<CredentialNotFoundException>(() =>
            _orchestrator.LaunchExe(@"C:\tools\bridge.exe", "S-1-5-21-0000000000-0000000000-0000000000-9999"));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void LaunchExe_Success_CallsProcessLaunchServiceWithCorrectParams()
    {
        var decryptedPassword = SetupDecryptableCredential();
        var exePath = @"C:\tools\bridge.exe";
        var args = new List<string> { "--copy", "--pipe", "TestPipe" };
        var workDir = @"C:\tools";

        _orchestrator.LaunchExe(exePath, TestSid, args, workDir);

        _processLaunchService.Verify(p => p.LaunchExe(
            new ProcessLaunchTarget(exePath, "--copy --pipe TestPipe", workDir, null),
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            default), Times.Once);
    }

    [Fact]
    public void Launch_DisposesPasswordEvenOnException()
    {
        var decryptedPassword = SetupDecryptableCredential();

        _processLaunchService
            .Setup(p => p.Launch(It.IsAny<AppEntry>(), It.IsAny<LaunchCredentials>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AppSettings?>(), It.IsAny<LaunchFlags>()))
            .Throws(new InvalidOperationException("test failure"));

        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath };

        Assert.Throws<InvalidOperationException>(() => _orchestrator.Launch(app, null));

        // Password should have been disposed in finally block
        Assert.Throws<ObjectDisposedException>(() => decryptedPassword.AppendChar('y'));
    }

    // --- AppContainer launch path ---

    private (AppLaunchOrchestrator orchestrator, Mock<IAppContainerService> containerService)
        CreateOrchestratorWithContainer()
    {
        var containerService = new Mock<IAppContainerService>();
        var orchestrator = new AppLaunchOrchestrator(
            _processLaunchService.Object, _encryptionService.Object,
            _sidResolver.Object, _permissionGrantService.Object, _accountLauncher.Object,
            containerService.Object, _folderHandlerService.Object,
            _databaseService.Object);
        orchestrator.SetData(new SessionContext
        {
            Database = _database,
            CredentialStore = _credentialStore,
            PinDerivedKey = _protectedPinKey
        });
        return (orchestrator, containerService);
    }

    [Fact]
    public void Launch_WithAppContainerName_SkipsDecryptAndResolve()
    {
        var (orchestrator, containerService) = CreateOrchestratorWithContainer();
        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser" };
        _database.AppContainers.Add(entry);

        var app = new AppEntry { ExePath = @"C:\apps\browser.exe", AccountSid = "", AppContainerName = "ram_browser" };

        orchestrator.Launch(app, null);

        // ICredentialEncryptionService.Decrypt must never be called — no credential resolution
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
        containerService.Verify(s => s.Launch(app, entry, null, null), Times.Once);
    }

    [Fact]
    public void Launch_WithAppContainerName_LooksUpCorrectEntryFromDatabase()
    {
        var (orchestrator, containerService) = CreateOrchestratorWithContainer();
        var entryA = new AppContainerEntry { Name = "ram_other", DisplayName = "Other" };
        var entryB = new AppContainerEntry { Name = "ram_target", DisplayName = "Target" };
        _database.AppContainers.Add(entryA);
        _database.AppContainers.Add(entryB);

        var app = new AppEntry { ExePath = @"C:\apps\target.exe", AccountSid = "", AppContainerName = "ram_target" };
        orchestrator.Launch(app, "--flag");

        // Must pass the correct container entry (entryB, not entryA)
        containerService.Verify(s => s.Launch(app, entryB, "--flag", null), Times.Once);
        containerService.Verify(s => s.Launch(app, entryA, It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Launch_WithUnknownAppContainerName_Throws()
    {
        var (orchestrator, _) = CreateOrchestratorWithContainer();
        // No container in database

        var app = new AppEntry { ExePath = @"C:\apps\browser.exe", AccountSid = "", AppContainerName = "ram_missing" };

        Assert.Throws<InvalidOperationException>(() => orchestrator.Launch(app, null));
    }

    [Fact]
    public void Launch_AppContainerName_NotInDatabase_ThrowsInvalidOperationException()
    {
        // The database has no AppContainerEntry for "ram_browser" → InvalidOperationException
        var app = new AppEntry
        {
            ExePath = @"C:\apps\browser.exe",
            AccountSid = "",
            AppContainerName = "ram_browser"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _orchestrator.Launch(app, null));

        Assert.Contains("ram_browser", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Launch_WithNullAppContainerName_UsesCredentialPath()
    {
        var (orchestrator, containerService) = CreateOrchestratorWithContainer();
        var decryptedPassword = SetupDecryptableCredential();
        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath };

        orchestrator.Launch(app, null);

        // Must use the normal credential path, not the container service
        containerService.Verify(s => s.Launch(It.IsAny<AppEntry>(), It.IsAny<AppContainerEntry>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
        _processLaunchService.Verify(p => p.Launch(
            app,
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            null, null, _database.Settings, default), Times.Once);
    }

    // --- LaunchFolderBrowser ---

    [Fact]
    public void LaunchFolderBrowser_CredentialNotFound_ThrowsCredentialNotFoundException()
    {
        var ex = Assert.Throws<CredentialNotFoundException>(() =>
            _orchestrator.LaunchFolderBrowser("S-1-5-21-0000000000-0000000000-0000000000-9999", @"C:\Folder"));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void LaunchFolderBrowser_ValidCredential_CallsLaunchFolderWithCorrectParams()
    {
        var decryptedPassword = SetupDecryptableCredential();
        var folderPath = @"C:\StartMenu";

        _orchestrator.LaunchFolderBrowser(TestSid, folderPath);

        _processLaunchService.Verify(p => p.LaunchFolder(
            folderPath,
            _database.Settings.FolderBrowserExePath,
            _database.Settings.FolderBrowserArguments,
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            default), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_LaunchAsLowIntegrity_PassesFlagThrough()
    {
        var decryptedPassword = SetupDecryptableCredential();
        var folderPath = @"C:\StartMenu";

        _orchestrator.LaunchFolderBrowser(TestSid, folderPath, launchAsLowIntegrity: true);

        _processLaunchService.Verify(p => p.LaunchFolder(
            folderPath,
            It.IsAny<string>(), It.IsAny<string>(),
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            new LaunchFlags(false, true)), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_DisposesPasswordEvenOnException()
    {
        var decryptedPassword = SetupDecryptableCredential();

        _processLaunchService
            .Setup(p => p.LaunchFolder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<LaunchCredentials>(), It.IsAny<LaunchFlags>()))
            .Throws(new InvalidOperationException("test failure"));

        Assert.Throws<InvalidOperationException>(() =>
            _orchestrator.LaunchFolderBrowser(TestSid, @"C:\Folder"));

        Assert.Throws<ObjectDisposedException>(() => decryptedPassword.AppendChar('y'));
    }

    // --- Split-token tri-state resolution ---

    /// <param name="runAsSplitToken">Per-app override (null = inherit account default)</param>
    /// <param name="removeFromOptOut">When true, TestSid is removed from SplitTokenOptOutSids so the account-level default is "use split token"</param>
    /// <param name="expectedSplitToken">Expected UseSplitToken in the LaunchFlags passed to the service</param>
    [Theory]
    [InlineData(true, false, true)] // explicit true → split token on
    [InlineData(false, true, false)] // explicit false → overrides account default (which is on when NOT in OptOut)
    [InlineData(null, true, true)] // null + account in defaults (not in OptOut) → split token on
    [InlineData(null, false, false)] // null + account NOT in defaults (in OptOut) → split token off
    public void Launch_SplitToken_TriStateResolution(bool? runAsSplitToken, bool removeFromOptOut, bool expectedSplitToken)
    {
        var decryptedPassword = SetupDecryptableCredential();
        if (removeFromOptOut)
            _database.GetOrCreateAccount(TestSid).SplitTokenOptOut = false;
        var app = new AppEntry { AccountSid = TestSid, RunAsSplitToken = runAsSplitToken, ExePath = _tempExePath };

        _orchestrator.Launch(app, null);

        _processLaunchService.Verify(p => p.Launch(
            app,
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            null, null, _database.Settings, new LaunchFlags(expectedSplitToken, false)), Times.Once);
    }

    // --- Low integrity tri-state resolution ---

    /// <param name="launchAsLowIntegrity">Per-app override (null = inherit account default)</param>
    /// <param name="addToLowIntegrityDefaults">When true, TestSid is added to LowIntegrityDefaultSids so the account-level default is "use low integrity"</param>
    /// <param name="expectedLowIntegrity">Expected UseLowIntegrity in the LaunchFlags passed to the service</param>
    [Theory]
    [InlineData(true, false, true)] // explicit true → low integrity on
    [InlineData(false, true, false)] // explicit false → overrides account default (which is on when in defaults)
    [InlineData(null, true, true)] // null + account in defaults → low integrity on
    [InlineData(null, false, false)] // null + account NOT in defaults → low integrity off
    public void Launch_LowIntegrity_TriStateResolution(bool? launchAsLowIntegrity, bool addToLowIntegrityDefaults, bool expectedLowIntegrity)
    {
        var decryptedPassword = SetupDecryptableCredential();
        if (addToLowIntegrityDefaults)
            _database.GetOrCreateAccount(TestSid).LowIntegrityDefault = true;
        var app = new AppEntry { AccountSid = TestSid, LaunchAsLowIntegrity = launchAsLowIntegrity, ExePath = _tempExePath };

        _orchestrator.Launch(app, null);

        _processLaunchService.Verify(p => p.Launch(
            app,
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            null, null, _database.Settings, new LaunchFlags(false, expectedLowIntegrity)), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_LowIntegrityNullAccountInDefaults_PassesTrueToService()
    {
        var decryptedPassword = SetupDecryptableCredential();
        _database.GetOrCreateAccount(TestSid).LowIntegrityDefault = true;
        var folderPath = @"C:\StartMenu";

        _orchestrator.LaunchFolderBrowser(TestSid, folderPath, launchAsLowIntegrity: null);

        _processLaunchService.Verify(p => p.LaunchFolder(
            folderPath, It.IsAny<string>(), It.IsAny<string>(),
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            new LaunchFlags(false, true)), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowser_SplitTokenAccountDefault_PassesTrueToService()
    {
        var decryptedPassword = SetupDecryptableCredential();
        _database.GetOrCreateAccount(TestSid).SplitTokenOptOut = false;
        var folderPath = @"C:\StartMenu";

        _orchestrator.LaunchFolderBrowser(TestSid, folderPath);

        _processLaunchService.Verify(p => p.LaunchFolder(
            folderPath, It.IsAny<string>(), It.IsAny<string>(),
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            new LaunchFlags(true, false)), Times.Once);
    }

    [Fact]
    public void LaunchDiscoveredApp_SplitTokenDefault_ResolvesAndPassesFlag()
    {
        var decryptedPassword = SetupDecryptableCredential();
        _database.GetOrCreateAccount(TestSid).SplitTokenOptOut = false;
        var tempFile = Path.GetTempFileName();
        try
        {
            _orchestrator.LaunchDiscoveredApp(tempFile, TestSid);

            _processLaunchService.Verify(p => p.LaunchExe(
                new ProcessLaunchTarget(tempFile, null, Path.GetDirectoryName(tempFile), null),
                new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
                new LaunchFlags(true, false)), Times.Once);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LaunchExe_LowIntegrityDefault_ResolvesAndPassesFlag()
    {
        var decryptedPassword = SetupDecryptableCredential();
        _database.GetOrCreateAccount(TestSid).LowIntegrityDefault = true;
        var exePath = @"C:\tools\bridge.exe";

        _orchestrator.LaunchExe(exePath, TestSid);

        _processLaunchService.Verify(p => p.LaunchExe(
            new ProcessLaunchTarget(exePath, null, null, null),
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            new LaunchFlags(false, true)), Times.Once);
    }

    [Fact]
    public void LaunchFolderBrowserFromTray_SplitTokenDefault_ResolvesAndPassesFlag()
    {
        var decryptedPassword = SetupDecryptableCredential();
        _database.GetOrCreateAccount(TestSid).SplitTokenOptOut = false;
        var sidResolver = new Mock<ISidResolver>();
        sidResolver.Setup(r => r.TryGetStartMenuProgramsPath(TestSid, false))
            .Returns(@"C:\StartMenu");
        var orchestrator = new AppLaunchOrchestrator(
            _processLaunchService.Object, _encryptionService.Object,
            sidResolver.Object, _permissionGrantService.Object, _accountLauncher.Object,
            _appContainerService.Object, _folderHandlerService.Object,
            _databaseService.Object);
        orchestrator.SetData(new SessionContext
        {
            Database = _database,
            CredentialStore = _credentialStore,
            PinDerivedKey = _protectedPinKey
        });

        orchestrator.LaunchFolderBrowserFromTray(TestSid);

        _processLaunchService.Verify(p => p.LaunchFolder(
            @"C:\StartMenu", It.IsAny<string>(), It.IsAny<string>(),
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            new LaunchFlags(true, false)), Times.Once);
    }

    // --- LaunchTerminalFromTray ---

    private AppLaunchOrchestrator CreateOrchestratorWithTerminalMocks(
        out Mock<IAccountLauncher> accountLauncher,
        out Mock<ISidResolver> sidResolver)
    {
        accountLauncher = new Mock<IAccountLauncher>();
        sidResolver = new Mock<ISidResolver>();
        var orchestrator = new AppLaunchOrchestrator(
            _processLaunchService.Object, _encryptionService.Object,
            sidResolver.Object, _permissionGrantService.Object, accountLauncher.Object,
            _appContainerService.Object, _folderHandlerService.Object,
            _databaseService.Object);
        orchestrator.SetData(new SessionContext
        {
            Database = _database,
            CredentialStore = _credentialStore,
            PinDerivedKey = _protectedPinKey
        });
        return orchestrator;
    }

    [Fact]
    public void LaunchTerminalFromTray_LaunchesWithResolvedTerminalAndProfilePath()
    {
        var decryptedPassword = SetupDecryptableCredential();
        var orchestrator = CreateOrchestratorWithTerminalMocks(out var accountLauncher, out var sidResolver);

        const string terminalExe = @"C:\Users\User\AppData\Local\Microsoft\WindowsApps\wt.exe";
        const string profilePath = @"C:\Users\User";
        accountLauncher.Setup(a => a.ResolveTerminalExe(TestSid)).Returns(terminalExe);
        sidResolver.Setup(r => r.TryGetProfilePath(TestSid)).Returns(profilePath);

        orchestrator.LaunchTerminalFromTray(TestSid);

        _processLaunchService.Verify(p => p.LaunchExe(
            new ProcessLaunchTarget(terminalExe, null, profilePath, null),
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            default), Times.Once);
    }

    [Fact]
    public void LaunchTerminalFromTray_SplitTokenDefault_PassesFlagThrough()
    {
        var decryptedPassword = SetupDecryptableCredential();
        _database.GetOrCreateAccount(TestSid).SplitTokenOptOut = false; // account default = split token on
        var orchestrator = CreateOrchestratorWithTerminalMocks(out var accountLauncher, out var sidResolver);

        accountLauncher.Setup(a => a.ResolveTerminalExe(TestSid)).Returns("cmd.exe");
        sidResolver.Setup(r => r.TryGetProfilePath(TestSid)).Returns(@"C:\Users\User");

        orchestrator.LaunchTerminalFromTray(TestSid);

        _processLaunchService.Verify(p => p.LaunchExe(
            new ProcessLaunchTarget("cmd.exe", null, @"C:\Users\User", null),
            new LaunchCredentials(decryptedPassword, "", "User", LaunchTokenSource.Credentials),
            new LaunchFlags(true, false)), Times.Once);
    }

    [Fact]
    public void LaunchTerminalFromTray_CredentialNotFound_ThrowsCredentialNotFoundException()
    {
        var orchestrator = CreateOrchestratorWithTerminalMocks(out _, out _);

        Assert.Throws<CredentialNotFoundException>(() =>
            orchestrator.LaunchTerminalFromTray("S-1-5-21-0000000000-0000000000-0000000000-9999"));
    }

    [Fact]
    public void LaunchTerminalFromTray_DisposesPasswordEvenOnException()
    {
        var decryptedPassword = SetupDecryptableCredential();
        var orchestrator = CreateOrchestratorWithTerminalMocks(out var accountLauncher, out var sidResolver);

        accountLauncher.Setup(a => a.ResolveTerminalExe(TestSid)).Returns("cmd.exe");
        sidResolver.Setup(r => r.TryGetProfilePath(TestSid)).Returns((string?)null);
        _processLaunchService
            .Setup(p => p.LaunchExe(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchCredentials>(),
                It.IsAny<LaunchFlags>()))
            .Throws(new InvalidOperationException("launch failure"));

        Assert.Throws<InvalidOperationException>(() =>
            orchestrator.LaunchTerminalFromTray(TestSid));

        Assert.Throws<ObjectDisposedException>(() => decryptedPassword.AppendChar('y'));
    }
}