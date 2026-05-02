using Moq;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.RunAs;
using RunFence.RunAs.UI;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for RunAsPostDialogRouter, verifying each result-type routing path.
/// </summary>
public class RunAsPostDialogRouterTests
{
    private const string FilePath = @"C:\Apps\tool.exe";
    private const string TestSid = "S-1-5-21-100-200-300-1001";

    private readonly Mock<IEvaluationLimitHelper> _evaluationLimitHelper = new();
    private readonly Mock<IRunAsUserAccountCreator> _userAccountCreator = new();
    private readonly Mock<IRunAsContainerCreator> _containerCreator = new();
    private readonly RunAsDosProtection _dosProtection;
    private readonly RunAsPostDialogRouter _router;

    public RunAsPostDialogRouterTests()
    {
        _dosProtection = new RunAsDosProtection(new Mock<IStopwatchProvider>().Object);

        _router = new RunAsPostDialogRouter(
            _userAccountCreator.Object,
            _containerCreator.Object,
            _evaluationLimitHelper.Object,
            _dosProtection);
    }

    private static RunAsDialogResult MakeCredentialResult(CredentialEntry? credential = null) =>
        new(
            Credential: credential ?? new CredentialEntry { Sid = TestSid },
            SelectedContainer: null,
            PermissionGrant: null,
            CreateAppEntryOnly: false,
            PrivilegeLevel: PrivilegeLevel.Basic,
            UpdateOriginalShortcut: false,
            RevertShortcutRequested: false,
            EditExistingApp: null,
            ExistingAppForLaunch: null);

    private static RunAsDialogResult MakeContainerResult(AppContainerEntry container) =>
        new(
            Credential: null,
            SelectedContainer: container,
            PermissionGrant: null,
            CreateAppEntryOnly: false,
            PrivilegeLevel: PrivilegeLevel.Basic,
            UpdateOriginalShortcut: false,
            RevertShortcutRequested: false,
            EditExistingApp: null,
            ExistingAppForLaunch: null);

    private static RunAsDialogResult MakeRevertResult() =>
        new(
            Credential: null,
            SelectedContainer: null,
            PermissionGrant: null,
            CreateAppEntryOnly: false,
            PrivilegeLevel: PrivilegeLevel.Basic,
            UpdateOriginalShortcut: false,
            RevertShortcutRequested: true,
            EditExistingApp: null,
            ExistingAppForLaunch: null);

    private static List<CredentialEntry> EmptyCredentials() => [];

    private static RunAsDialogResult MakeEmptyCapturedResult() =>
        new(Credential: null, SelectedContainer: null, PermissionGrant: null,
            CreateAppEntryOnly: false, PrivilegeLevel: PrivilegeLevel.Basic,
            UpdateOriginalShortcut: false, RevertShortcutRequested: false,
            EditExistingApp: null, ExistingAppForLaunch: null);

    // ── DialogResult.Cancel ──────────────────────────────────────────────────

    [Fact]
    public void Route_DialogResultCancel_ReturnsEmptyResult()
    {
        // Arrange
        var capturedResult = MakeCredentialResult();

        // Act
        var result = _router.Route(DialogResult.Cancel, capturedResult,
            createNewAccountRequested: false, createNewContainerRequested: false,
            FilePath, EmptyCredentials());

        // Assert
        Assert.Null(result.Credential);
        Assert.Null(result.SelectedContainer);
    }

    [Fact]
    public void Route_NullCapturedResult_ReturnsEmptyResult()
    {
        // Act
        var result = _router.Route(DialogResult.OK, null,
            createNewAccountRequested: false, createNewContainerRequested: false,
            FilePath, EmptyCredentials());

        // Assert
        Assert.Null(result.Credential);
        Assert.Null(result.SelectedContainer);
    }

    // ── RevertShortcutRequested ──────────────────────────────────────────────

    [Fact]
    public void Route_RevertShortcutRequested_ReturnsCapturedResultDirectly()
    {
        // Arrange
        var capturedResult = MakeRevertResult();

        // Act
        var result = _router.Route(DialogResult.OK, capturedResult,
            createNewAccountRequested: false, createNewContainerRequested: false,
            FilePath, EmptyCredentials());

        // Assert
        Assert.True(result.RevertShortcutRequested);
        Assert.Same(capturedResult, result);
    }

    // ── SelectedContainer already set ────────────────────────────────────────

    [Fact]
    public void Route_SelectedContainerAlreadySet_ReturnsCapturedResultDirectly()
    {
        // Arrange
        var container = new AppContainerEntry { Name = "TestContainer" };
        var capturedResult = MakeContainerResult(container);

        // Act
        var result = _router.Route(DialogResult.OK, capturedResult,
            createNewAccountRequested: false, createNewContainerRequested: false,
            FilePath, EmptyCredentials());

        // Assert — returns same result without creating a new container
        Assert.Same(container, result.SelectedContainer);
        Assert.Same(capturedResult, result);
    }

    // ── Existing credential selected ─────────────────────────────────────────

    [Fact]
    public void Route_ExistingCredentialSelected_ReturnsCapturedResult()
    {
        // Arrange
        var credential = new CredentialEntry { Sid = TestSid };
        var capturedResult = MakeCredentialResult(credential);

        // Act
        var result = _router.Route(DialogResult.OK, capturedResult,
            createNewAccountRequested: false, createNewContainerRequested: false,
            FilePath, EmptyCredentials());

        // Assert
        Assert.Same(credential, result.Credential);
        Assert.Same(capturedResult, result);
    }

    [Fact]
    public void Route_NullCredentialAndNoNewAccountOrContainer_ReturnsEmpty()
    {
        // Arrange — result with no credential, no container, no new account
        var capturedResult = MakeEmptyCapturedResult();

        // Act
        var result = _router.Route(DialogResult.OK, capturedResult,
            createNewAccountRequested: false, createNewContainerRequested: false,
            FilePath, EmptyCredentials());

        // Assert
        Assert.Null(result.Credential);
        Assert.Null(result.SelectedContainer);
    }

    // ── CreateNewAccount: license guard ──────────────────────────────────────

    [Fact]
    public void Route_CreateNewAccountRequested_LicenseLimitExceeded_ReturnsEmptyWithoutAccountCreation()
    {
        // Arrange — license check fails before CreateNewAccount is called
        _evaluationLimitHelper.Setup(e => e.CheckCredentialLimit(
            It.IsAny<List<CredentialEntry>>(), It.IsAny<IWin32Window?>(), It.IsAny<string?>()))
            .Returns(false);

        var capturedResult = MakeEmptyCapturedResult();

        // Act
        var result = _router.Route(DialogResult.OK, capturedResult,
            createNewAccountRequested: true, createNewContainerRequested: false,
            FilePath, EmptyCredentials());

        // Assert — empty result returned; account creation was blocked by license check
        Assert.Null(result.Credential);
        Assert.Null(result.SelectedContainer);
        _userAccountCreator.Verify(
            x => x.CreateNewAccount(It.IsAny<string>(), out It.Ref<AncestorPermissionResult?>.IsAny),
            Times.Never);
    }

    [Fact]
    public void Route_CreateNewAccountRequested_LicenseOk_AccountCreationCancelled_ReturnsEmpty()
    {
        // Arrange — license check passes, but account creation is cancelled by user
        _evaluationLimitHelper.Setup(e => e.CheckCredentialLimit(
            It.IsAny<List<CredentialEntry>>(), It.IsAny<IWin32Window?>(), It.IsAny<string?>()))
            .Returns(true);
        AncestorPermissionResult? outGrant = null;
        _userAccountCreator
            .Setup(x => x.CreateNewAccount(It.IsAny<string>(), out outGrant))
            .Returns((CredentialEntry?)null);

        var capturedResult = MakeEmptyCapturedResult();

        // Act
        var result = _router.Route(DialogResult.OK, capturedResult,
            createNewAccountRequested: true, createNewContainerRequested: false,
            FilePath, EmptyCredentials());

        // Assert — empty result; account creation was attempted but cancelled
        Assert.Null(result.Credential);
        Assert.Null(result.SelectedContainer);
        _userAccountCreator.Verify(
            x => x.CreateNewAccount(It.IsAny<string>(), out It.Ref<AncestorPermissionResult?>.IsAny),
            Times.Once);
    }

    // ── CreateNewContainer ────────────────────────────────────────────────────

    [Fact]
    public void Route_CreateNewContainerRequested_CreatesContainerAndReturnsItInResult()
    {
        // Arrange
        var newContainer = new AppContainerEntry { Name = "NewContainer" };
        _containerCreator.Setup(c => c.CreateNewContainer()).Returns(newContainer);

        var capturedResult = MakeEmptyCapturedResult();

        // Act
        var result = _router.Route(DialogResult.OK, capturedResult,
            createNewAccountRequested: false, createNewContainerRequested: true,
            FilePath, EmptyCredentials());

        // Assert
        Assert.Same(newContainer, result.SelectedContainer);
        Assert.Null(result.Credential);
        _containerCreator.Verify(c => c.CreateNewContainer(), Times.Once);
    }

    [Fact]
    public void Route_CreateNewContainerRequested_ContainerCreationCancelled_ReturnsEmpty()
    {
        // Arrange — CreateNewContainer returns null (user cancelled)
        _containerCreator.Setup(c => c.CreateNewContainer()).Returns((AppContainerEntry?)null);

        var capturedResult = MakeEmptyCapturedResult();

        // Act
        var result = _router.Route(DialogResult.OK, capturedResult,
            createNewAccountRequested: false, createNewContainerRequested: true,
            FilePath, EmptyCredentials());

        // Assert
        Assert.Null(result.SelectedContainer);
        Assert.Null(result.Credential);
    }

    // ── Ordering: RevertShortcut takes priority over createNewContainer ───────

    [Fact]
    public void Route_RevertRequested_TakesPriorityOverContainerRequested()
    {
        // Arrange — both revert and new container flags set; revert wins
        var capturedResult = MakeRevertResult();

        // Act
        var result = _router.Route(DialogResult.OK, capturedResult,
            createNewAccountRequested: false, createNewContainerRequested: true,
            FilePath, EmptyCredentials());

        // Assert
        Assert.True(result.RevertShortcutRequested);
    }
}
