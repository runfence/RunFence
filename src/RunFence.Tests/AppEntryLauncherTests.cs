using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public class AppEntryLauncherTests : IDisposable
{
    private readonly Mock<ILaunchFacade> _facade;
    private readonly AppEntryLauncher _launcher;
    private readonly AppDatabase _database;
    private readonly byte[] _pinDerivedKey = new byte[32];
    private readonly SecureSecret _protectedPinKey;

    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private readonly string _tempExePath;

    public AppEntryLauncherTests()
    {
        _tempExePath = Path.GetTempFileName();
        _facade = new Mock<ILaunchFacade>();
        var sessionProvider = new Mock<ISessionProvider>();

        _database = new AppDatabase
        {
            SidNames = { [TestSid] = "User" }
        };
        var credentialStore = new CredentialStore();
        _protectedPinKey = TestSecretFactory.FromBytes(_pinDerivedKey);

        sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
            Database = _database,
            CredentialStore = credentialStore,
        }.WithOwnedPinDerivedKey(_protectedPinKey));

        _launcher = new AppEntryLauncher(
            _facade.Object,
            new AppEntryLaunchPlanBuilder(),
            sessionProvider.Object);
    }

    public void Dispose()
    {
        _protectedPinKey.Dispose();
        try { File.Delete(_tempExePath); }
        catch { }
    }

    private static LaunchExecutionResult MakeLaunchResult(params string[] warnings)
        => new(
            warnings.Length == 0 ? LaunchExecutionStatus.ProcessStarted : LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings,
            null,
            warnings);

    // --- Normal file launch ---

    [Fact]
    public void Launch_NormalApp_CallsFacadeLaunchFile()
    {
        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath };

        using var result = _launcher.Launch(app, "--flag");

        _facade.Verify(f => f.LaunchFile(It.Is<ProcessLaunchTarget>(t => t.ExePath == _tempExePath),
            It.Is<AccountLaunchIdentity>(a => a.Sid == TestSid), null), Times.Once);
    }

    [Fact]
    public void Launch_NormalApp_MarksInitialTargetApproved()
    {
        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath, AllowPassingArguments = true };

        _launcher.Launch(app, "--flag");

        _facade.Verify(f => f.LaunchFile(
            It.Is<ProcessLaunchTarget>(t =>
                t.ExePath == _tempExePath &&
                t.Arguments == "--flag" &&
                t.IsPathApproved),
            It.IsAny<AccountLaunchIdentity>(),
            null), Times.Once);
    }

    [Fact]
    public void Launch_NormalApp_ReturnsFacadeResult()
    {
        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath };
        var expected = MakeLaunchResult("post-launch warning");
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Returns(expected);

        using var result = _launcher.Launch(app, null);

        Assert.Same(expected, result);
    }

    [Fact]
    public void Launch_NormalApp_PassesLauncherArgumentsWhenAllowed()
    {
        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath, AllowPassingArguments = true };

        _launcher.Launch(app, "--custom-arg");

        _facade.Verify(f => f.LaunchFile(It.Is<ProcessLaunchTarget>(t => t.Arguments == "--custom-arg"),
            It.IsAny<AccountLaunchIdentity>(), null), Times.Once);
    }

    [Fact]
    public void Launch_FacadeThrowsCredentialNotFoundException_Propagates()
    {
        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath };
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Throws(new CredentialNotFoundException("Account not found."));

        var ex = Assert.Throws<CredentialNotFoundException>(() => _launcher.Launch(app, null));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void Launch_FacadeThrowsMissingPasswordException_Propagates()
    {
        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath };
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Throws(new MissingPasswordException("No password stored."));

        Assert.Throws<MissingPasswordException>(() => _launcher.Launch(app, null));
    }

    [Fact]
    public void Launch_FacadeThrowsGrantOperationException_Propagates()
    {
        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath };
        var exception = new GrantOperationException(
            GrantApplyFailureStep.GrantAclApply,
            _tempExePath,
            null,
            new UnauthorizedAccessException("denied"));
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Throws(exception);

        var actual = Assert.Throws<GrantOperationException>(() => _launcher.Launch(app, null));

        Assert.Same(exception, actual);
    }

    // --- URL scheme launch ---

    [Fact]
    public void Launch_UrlSchemeApp_CallsFacadeLaunchUrl()
    {
        var app = new AppEntry { AccountSid = TestSid, ExePath = "https://example.com", IsUrlScheme = true };

        _launcher.Launch(app, null);

        _facade.Verify(f => f.LaunchUrl("https://example.com",
            It.Is<AccountLaunchIdentity>(a => a.Sid == TestSid)), Times.Once);
        _facade.Verify(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()), Times.Never);
    }

    // --- Folder app launch ---

    [Fact]
    public void Launch_FolderApp_CallsFacadeLaunchFolderBrowser()
    {
        var folderPath = @"C:\Users\User\Documents";
        var app = new AppEntry { AccountSid = TestSid, ExePath = folderPath, IsFolder = true };

        _launcher.Launch(app, null);

        _facade.Verify(f => f.LaunchFolderBrowser(
            It.Is<AccountLaunchIdentity>(a => a.Sid == TestSid),
            folderPath, null, true), Times.Once);
        _facade.Verify(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()), Times.Never);
    }

    [Fact]
    public void Launch_AssociationResolvedFolderApp_KeepsConfiguredFolderApproved()
    {
        var folderPath = @"C:\Users\User\Documents";
        var app = new AppEntry { AccountSid = TestSid, ExePath = folderPath, IsFolder = true };

        _launcher.Launch(app, "--assoc-target", associationArgsTemplate: "");

        _facade.Verify(f => f.LaunchFolderBrowser(
            It.Is<AccountLaunchIdentity>(a => a.Sid == TestSid),
            folderPath,
            null,
            true), Times.Once);
    }

    [Fact]
    public void Launch_AssociationResolvedApp_MarksInitialTargetUnapproved()
    {
        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath, AllowPassingArguments = true };

        _launcher.Launch(app, "--assoc-target", associationArgsTemplate: "");

        _facade.Verify(f => f.LaunchFile(
            It.Is<ProcessLaunchTarget>(t =>
                t.ExePath == _tempExePath &&
                t.Arguments == "--assoc-target" &&
                !t.IsPathApproved),
            It.IsAny<AccountLaunchIdentity>(),
            null), Times.Once);
    }

    // --- AppContainer launch path ---

    [Fact]
    public void Launch_WithAppContainerName_CallsFacadeLaunchFileWithContainerArgs()
    {
        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser" };
        _database.AppContainers.Add(entry);
        var app = new AppEntry { ExePath = @"C:\apps\browser.exe", AccountSid = "", AppContainerName = "ram_browser" };

        _launcher.Launch(app, null);

        _facade.Verify(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.Is<AppContainerLaunchIdentity>(a => a.Entry == entry), null), Times.Once);
    }

    [Fact]
    public void Launch_WithAppContainerName_LooksUpCorrectEntry()
    {
        var entryA = new AppContainerEntry { Name = "ram_other", DisplayName = "Other" };
        var entryB = new AppContainerEntry { Name = "ram_target", DisplayName = "Target" };
        _database.AppContainers.Add(entryA);
        _database.AppContainers.Add(entryB);
        var app = new AppEntry { ExePath = @"C:\apps\target.exe", AccountSid = "", AppContainerName = "ram_target" };

        _launcher.Launch(app, "--flag");

        _facade.Verify(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.Is<AppContainerLaunchIdentity>(a => a.Entry == entryB), null), Times.Once);
        _facade.Verify(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.Is<AppContainerLaunchIdentity>(a => a.Entry == entryA), null), Times.Never);
    }

    [Fact]
    public void Launch_WithUnknownAppContainerName_ThrowsInvalidOperationException()
    {
        var app = new AppEntry { ExePath = @"C:\apps\browser.exe", AccountSid = "", AppContainerName = "ram_missing" };

        var ex = Assert.Throws<InvalidOperationException>(() => _launcher.Launch(app, null));

        Assert.Contains("ram_missing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- Null/default privilege level passing ---

    [Theory]
    [InlineData(PrivilegeLevel.Isolated)]
    [InlineData(PrivilegeLevel.HighestAllowed)]
    [InlineData(PrivilegeLevel.LowIntegrity)]
    [InlineData(PrivilegeLevel.Basic)]
    [InlineData(null)]
    public void Launch_PassesPrivilegeLevelToFacade(PrivilegeLevel? privilegeLevel)
    {
        var app = new AppEntry
        {
            AccountSid = TestSid,
            ExePath = _tempExePath,
            PrivilegeLevel = privilegeLevel,
        };

        _launcher.Launch(app, null);

        _facade.Verify(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(),
            It.Is<AccountLaunchIdentity>(a => a.PrivilegeLevel == privilegeLevel), null), Times.Once);
    }

    // --- permissionPrompt forwarding ---

    [Fact]
    public void Launch_NormalApp_ForwardsPermissionPromptToLaunchFile()
    {
        var app = new AppEntry { AccountSid = TestSid, ExePath = _tempExePath };
        Func<string, string, bool> prompt = (_, _) => true;

        _launcher.Launch(app, null, permissionPrompt: prompt);

        _facade.Verify(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(),
            It.IsAny<AccountLaunchIdentity>(), prompt), Times.Once);
    }

    [Fact]
    public void Launch_ContainerApp_ForwardsPermissionPromptToLaunchFile()
    {
        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser" };
        _database.AppContainers.Add(entry);
        var app = new AppEntry { ExePath = @"C:\apps\browser.exe", AccountSid = "", AppContainerName = "ram_browser" };
        Func<string, string, bool> prompt = (_, _) => true;

        _launcher.Launch(app, null, permissionPrompt: prompt);

        _facade.Verify(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(),
            It.IsAny<AppContainerLaunchIdentity>(), prompt), Times.Once);
    }

    // ── TC-30: AllowPassingArguments=false ───────────────────────────────────

    [Fact]
    public void Launch_AllowPassingArgumentsFalse_UsesDefaultArgumentsOnly()
    {
        // Arrange — app has AllowPassingArguments=false and DefaultArguments; launcher passes "--custom-arg".
        // Expected: the launcher argument is ignored; DefaultArguments is used instead.
        var app = new AppEntry
        {
            AccountSid = TestSid,
            ExePath = _tempExePath,
            AllowPassingArguments = false,
            DefaultArguments = "--default-arg"
        };

        // Act
        _launcher.Launch(app, "--custom-arg");

        // Assert — target uses DefaultArguments, not the launcher arguments
        _facade.Verify(f => f.LaunchFile(
            It.Is<ProcessLaunchTarget>(t => t.Arguments == "--default-arg"),
            It.IsAny<AccountLaunchIdentity>(), null), Times.Once);
    }

    [Fact]
    public void Launch_AllowPassingArgumentsFalse_NullDefault_LaunchesWithNullArguments()
    {
        // Arrange — AllowPassingArguments=false, no DefaultArguments, launcher passes arguments.
        // Expected: null arguments (launcher args discarded, no default to substitute).
        var app = new AppEntry
        {
            AccountSid = TestSid,
            ExePath = _tempExePath,
            AllowPassingArguments = false
            // DefaultArguments defaults to string.Empty — DetermineArguments returns null for empty default
        };

        // Act
        _launcher.Launch(app, "--should-be-ignored");

        // Assert — null arguments passed to facade
        _facade.Verify(f => f.LaunchFile(
            It.Is<ProcessLaunchTarget>(t => t.Arguments == null),
            It.IsAny<AccountLaunchIdentity>(), null), Times.Once);
    }

    [Fact]
    public void Launch_AllowPassingArgumentsNotSet_DefaultFalse_UsesDefaultArguments()
    {
        // Arrange — AllowPassingArguments defaults to false when not explicitly set.
        var app = new AppEntry
        {
            AccountSid = TestSid,
            ExePath = _tempExePath,
            DefaultArguments = "--default-only"
        };

        // Act — launcher passes arguments, but AllowPassingArguments=false (default)
        _launcher.Launch(app, "--ignored");

        // Assert — DefaultArguments used, not the launcher argument
        _facade.Verify(f => f.LaunchFile(
            It.Is<ProcessLaunchTarget>(t => t.Arguments == "--default-only"),
            It.IsAny<AccountLaunchIdentity>(), null), Times.Once);
    }
}
