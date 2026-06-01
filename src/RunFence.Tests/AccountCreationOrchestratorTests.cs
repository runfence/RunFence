using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.Forms;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.PrefTrans;
using RunFence.Tests.Helpers;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class AccountCreationOrchestratorTests : IDisposable
{
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);

    public void Dispose() => _pinKey.Dispose();

    [Fact]
    public void OpenCreateUserDialog_SaveFailedAfterMutationWithoutPendingSetup_RefreshesWithoutSaving()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            const string sid = "S-1-5-21-100-200-300-1001";
            var credId = Guid.NewGuid();
            var session = new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithClonedPinDerivedKey(_pinKey);

            var commitService = new Mock<IAccountCreationCommitService>();
            commitService.Setup(s => s.Commit(It.IsAny<AccountCreationData>(), session.Database))
                .Returns(new AccountCreationCommitOutcome(
                    AccountCreationCommitStatus.SaveFailedAfterMutation,
                    new AccountCreationCommitResult(credId, false, false),
                    new AccountCreationRollbackState
                    {
                        CreatedAccount = new CreatedAccountRollbackState
                        {
                            Sid = sid,
                            Username = "newuser",
                            HadPreviousAccount = false,
                            HadPreviousSidName = false,
                            HadPreviousFirewallSettings = false
                        },
                        PreviousSettings = session.Database.Settings.Clone(),
                    },
                    "save failed"));

            var rollbackService = CreateRollbackService();
            using var dialog = new TestAccountCreationDialog(sid)
            {
                Attempts =
                [
                    new TestAccountCreationDialog.AttemptState(
                        FirewallSettingsChanged: false,
                        SettingsImportPath: null,
                        AllowInternet: true)
                ]
            };
            var panelContext = new Mock<IAccountsPanelOperationContext>();
            panelContext.SetupGet(c => c.OwnerControl).Returns(new Panel());
            var messageBoxService = new Mock<IAccountMessageBoxService>();

            var orchestrator = CreateOrchestrator(
                session,
                commitService.Object,
                rollbackService,
                () => dialog,
                panelContext.Object,
                messageBoxService.Object);

            await orchestrator.OpenCreateUserDialog();

            panelContext.Verify(c => c.RefreshAndNotifyDataChanged(credId, -1), Times.Once);
            panelContext.Verify(c => c.SaveAndRefresh(It.IsAny<Guid?>(), It.IsAny<int>()), Times.Never);
            messageBoxService.Verify(
                m => m.Show(
                    It.IsAny<IWin32Window?>(),
                    It.Is<string>(text => text.Contains("remains available only in memory", StringComparison.Ordinal)),
                    "Warnings",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1),
                Times.Once);
        });
    }

    [Fact]
    public void OpenCreateUserDialog_PreSaveCommitFailureWithoutPendingSetup_RollsBackAndKeepsDialogRetryableUntilCancel()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            const string sid = "S-1-5-21-100-200-300-1001";
            var session = new SessionContext
{
                Database = new AppDatabase
                {
                    Accounts =
                    [
                        new AccountEntry
                        {
                            Sid = sid,
                            PrivilegeLevel = PrivilegeLevel.HighestAllowed
                        }
                    ],
                    SidNames =
                    {
                        [sid] = "newuser"
                    }
                },
                CredentialStore = new CredentialStore(),
            }.WithClonedPinDerivedKey(_pinKey);

            var commitService = new Mock<IAccountCreationCommitService>();
            commitService.Setup(s => s.Commit(It.IsAny<AccountCreationData>(), session.Database))
                .Returns(new AccountCreationCommitOutcome(
                    AccountCreationCommitStatus.SaveFailedAfterMutation,
                    Result: null,
                    RollbackState: new AccountCreationRollbackState
                    {
                        CreatedAccount = new CreatedAccountRollbackState
                        {
                            Sid = sid,
                            Username = "newuser",
                            HadPreviousAccount = false,
                            HadPreviousSidName = false,
                            HadPreviousFirewallSettings = false
                        },
                        PreviousSettings = session.Database.Settings.Clone(),
                    },
                    "association failed"));

            using var dialog = new TestAccountCreationDialog(sid)
            {
                Attempts =
                [
                    new TestAccountCreationDialog.AttemptState(
                        FirewallSettingsChanged: false,
                        SettingsImportPath: null,
                        AllowInternet: true)
                ]
            };
            var panelContext = new Mock<IAccountsPanelOperationContext>();
            panelContext.SetupGet(c => c.OwnerControl).Returns(new Panel());
            var messageBoxService = new Mock<IAccountMessageBoxService>();

            var orchestrator = CreateOrchestrator(
                session,
                commitService.Object,
                CreateRollbackService(),
                () => dialog,
                panelContext.Object,
                messageBoxService.Object);

            await orchestrator.OpenCreateUserDialog();

            Assert.Null(session.Database.GetAccount(sid));
            Assert.DoesNotContain(sid, session.Database.SidNames.Keys);
            Assert.Equal(1, dialog.CreateConfirmCallCount);
            panelContext.Verify(c => c.RefreshAndNotifyDataChanged(It.IsAny<Guid?>(), It.IsAny<int>()), Times.Never);
            panelContext.Verify(c => c.SaveAndRefresh(It.IsAny<Guid?>(), It.IsAny<int>()), Times.Never);
            messageBoxService.Verify(
                m => m.Show(
                    It.IsAny<IWin32Window?>(),
                    It.Is<string>(text => text.Contains("was rolled back", StringComparison.Ordinal)),
                    "Account Creation Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1),
                Times.Once);
        });
    }

    [Fact]
    public void OpenCreateUserDialog_SaveFailedAfterMutationWithPendingSetupAndRollbackFailure_RefreshesScheduledCleanup()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            const string sid = "S-1-5-21-100-200-300-1001";
            var credId = Guid.NewGuid();
            var session = new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithClonedPinDerivedKey(_pinKey);

            var commitService = new Mock<IAccountCreationCommitService>();
            commitService.Setup(s => s.Commit(It.IsAny<AccountCreationData>(), session.Database))
                .Returns(new AccountCreationCommitOutcome(
                    AccountCreationCommitStatus.SaveFailedAfterMutation,
                    new AccountCreationCommitResult(credId, false, false),
                    new AccountCreationRollbackState
                    {
                        CreatedAccount = new CreatedAccountRollbackState
                        {
                            Sid = sid,
                            Username = "newuser",
                            HadPreviousAccount = false,
                            HadPreviousSidName = false,
                            HadPreviousFirewallSettings = false
                        },
                        PreviousSettings = session.Database.Settings.Clone(),
                    },
                    "save failed"));

            var rollbackService = CreateRollbackService(deleteSucceeded: false);
            using var dialog = new TestAccountCreationDialog(sid)
            {
                Attempts =
                [
                    new TestAccountCreationDialog.AttemptState(
                        FirewallSettingsChanged: true,
                        SettingsImportPath: null,
                        AllowInternet: true)
                ]
            };
            var panelContext = new Mock<IAccountsPanelOperationContext>();
            panelContext.SetupGet(c => c.OwnerControl).Returns(new Panel());
            var messageBoxService = new Mock<IAccountMessageBoxService>();

            var orchestrator = CreateOrchestrator(
                session,
                commitService.Object,
                rollbackService,
                () => dialog,
                panelContext.Object,
                messageBoxService.Object);

            await orchestrator.OpenCreateUserDialog();

            Assert.NotNull(session.Database.GetAccount(sid)?.DeleteAfterUtc);
            panelContext.Verify(c => c.RefreshAndNotifyDataChanged(credId, -1), Times.Once);
            panelContext.Verify(c => c.SaveAndRefresh(It.IsAny<Guid?>(), It.IsAny<int>()), Times.Never);
            messageBoxService.Verify(
                m => m.Show(
                    It.IsAny<IWin32Window?>(),
                    It.Is<string>(text => text.Contains("rollback also failed", StringComparison.Ordinal)),
                    "Account Creation Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1),
                Times.Once);
        });
    }

    [Fact]
    public void OpenCreateUserDialog_SaveFailedAfterMutationWithPendingSetup_RollsBackAndKeepsDialogRetryableUntilCancel()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            const string sid = "S-1-5-21-100-200-300-1001";
            var credId = Guid.NewGuid();
            var session = new SessionContext
{
                Database = new AppDatabase
                {
                    Accounts =
                    [
                        new AccountEntry
                        {
                            Sid = sid,
                            PrivilegeLevel = PrivilegeLevel.HighestAllowed
                        }
                    ],
                    SidNames =
                    {
                        [sid] = "newuser"
                    }
                },
                CredentialStore = new CredentialStore(),
            }.WithClonedPinDerivedKey(_pinKey);

            var commitService = new Mock<IAccountCreationCommitService>();
            commitService.Setup(s => s.Commit(It.IsAny<AccountCreationData>(), session.Database))
                .Returns(new AccountCreationCommitOutcome(
                    AccountCreationCommitStatus.SaveFailedAfterMutation,
                    new AccountCreationCommitResult(credId, false, false),
                    new AccountCreationRollbackState
                    {
                        CreatedAccount = new CreatedAccountRollbackState
                        {
                            Sid = sid,
                            Username = "newuser",
                            HadPreviousAccount = false,
                            HadPreviousSidName = false,
                            HadPreviousFirewallSettings = false
                        },
                        PreviousSettings = session.Database.Settings.Clone(),
                    },
                    "save failed"));

            var rollbackService = CreateRollbackService();
            using var dialog = new TestAccountCreationDialog(sid)
            {
                Attempts =
                [
                    new TestAccountCreationDialog.AttemptState(
                        FirewallSettingsChanged: true,
                        SettingsImportPath: null,
                        AllowInternet: true)
                ]
            };
            var panelContext = new Mock<IAccountsPanelOperationContext>();
            panelContext.SetupGet(c => c.OwnerControl).Returns(new Panel());
            var messageBoxService = new Mock<IAccountMessageBoxService>();

            var orchestrator = CreateOrchestrator(
                session,
                commitService.Object,
                rollbackService,
                () => dialog,
                panelContext.Object,
                messageBoxService.Object);

            await orchestrator.OpenCreateUserDialog();

            Assert.Null(session.Database.GetAccount(sid));
            Assert.DoesNotContain(sid, session.Database.SidNames.Keys);
            Assert.Equal(1, dialog.CreateConfirmCallCount);
            Assert.Throws<ObjectDisposedException>(() => dialog.AttemptPasswords[0].Length);
            panelContext.Verify(c => c.RefreshAndNotifyDataChanged(It.IsAny<Guid?>(), It.IsAny<int>()), Times.Never);
            panelContext.Verify(c => c.SaveAndRefresh(It.IsAny<Guid?>(), It.IsAny<int>()), Times.Never);
            messageBoxService.Verify(
                m => m.Show(
                    It.IsAny<IWin32Window?>(),
                    It.Is<string>(text => text.Contains("was rolled back", StringComparison.Ordinal)),
                    "Account Creation Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1),
                Times.Once);
        });
    }

    [Fact]
    public void OpenCreateUserDialog_SaveFailedAfterMutationWithPendingSetup_RetriesWithinSameDialogAndCommitsOnceSucceeded()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            const string sid = "S-1-5-21-100-200-300-1001";
            var session = new SessionContext
{
                Database = new AppDatabase
                {
                    Accounts =
                    [
                        new AccountEntry
                        {
                            Sid = sid,
                            PrivilegeLevel = PrivilegeLevel.HighestAllowed
                        }
                    ],
                    SidNames =
                    {
                        [sid] = "newuser"
                    }
                },
                CredentialStore = new CredentialStore(),
            }.WithClonedPinDerivedKey(_pinKey);

            var credId = Guid.NewGuid();
            var commitService = new Mock<IAccountCreationCommitService>();
            commitService.SetupSequence(s => s.Commit(It.IsAny<AccountCreationData>(), session.Database))
                .Returns(new AccountCreationCommitOutcome(
                    AccountCreationCommitStatus.SaveFailedAfterMutation,
                    new AccountCreationCommitResult(Guid.NewGuid(), false, false),
                    new AccountCreationRollbackState
                    {
                        CreatedAccount = new CreatedAccountRollbackState
                        {
                            Sid = sid,
                            Username = "newuser",
                            HadPreviousAccount = false,
                            HadPreviousSidName = false,
                            HadPreviousFirewallSettings = false
                        },
                        PreviousSettings = session.Database.Settings.Clone(),
                    },
                    "save failed"))
                .Returns(new AccountCreationCommitOutcome(
                    AccountCreationCommitStatus.Succeeded,
                    new AccountCreationCommitResult(credId, false, false),
                    RollbackState: null,
                    ErrorMessage: null));

            var rollbackService = CreateRollbackService();
            using var dialog = new TestAccountCreationDialog(sid)
            {
                Attempts =
                [
                    new TestAccountCreationDialog.AttemptState(
                        FirewallSettingsChanged: true,
                        SettingsImportPath: null,
                        AllowInternet: true),
                    new TestAccountCreationDialog.AttemptState(
                        FirewallSettingsChanged: false,
                        SettingsImportPath: null,
                        AllowInternet: true)
                ]
            };
            var panelContext = new Mock<IAccountsPanelOperationContext>();
            panelContext.SetupGet(c => c.OwnerControl).Returns(new Panel());
            var messageBoxService = new Mock<IAccountMessageBoxService>();

            var orchestrator = CreateOrchestrator(
                session,
                commitService.Object,
                rollbackService,
                () => dialog,
                panelContext.Object,
                messageBoxService.Object);

            await orchestrator.OpenCreateUserDialog();

            Assert.Equal(2, dialog.CreateConfirmCallCount);
            Assert.Equal(2, commitService.Invocations.Count(i => i.Method.Name == nameof(IAccountCreationCommitService.Commit)));
            messageBoxService.Verify(
                m => m.Show(
                    It.IsAny<IWin32Window?>(),
                    It.Is<string>(text => text.Contains("was rolled back", StringComparison.Ordinal)),
                    "Account Creation Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1),
                Times.Once);
            Assert.Throws<ObjectDisposedException>(() => dialog.AttemptPasswords[0].Length);
            Assert.Throws<ObjectDisposedException>(() => dialog.LastTransferredPassword!.Length);

        });
    }

    [Fact]
    public void OpenCreateUserDialog_WhenCommitThrows_KeepsDialogRetryableUntilCancel()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            const string sid = "S-1-5-21-100-200-300-1001";
            var session = new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithClonedPinDerivedKey(_pinKey);

            var commitService = new Mock<IAccountCreationCommitService>();
            commitService.Setup(s => s.Commit(It.IsAny<AccountCreationData>(), session.Database))
                .Throws(new InvalidOperationException("commit exploded"));

            using var dialog = new TestAccountCreationDialog(sid)
            {
                Attempts =
                [
                    new TestAccountCreationDialog.AttemptState(
                        FirewallSettingsChanged: true,
                        SettingsImportPath: null,
                        AllowInternet: true)
                ]
            };
            var panelContext = new Mock<IAccountsPanelOperationContext>();
            panelContext.SetupGet(c => c.OwnerControl).Returns(new Panel());
            var messageBoxService = new Mock<IAccountMessageBoxService>();

            var orchestrator = CreateOrchestrator(
                session,
                commitService.Object,
                CreateRollbackService(),
                () => dialog,
                panelContext.Object,
                messageBoxService.Object);

            await orchestrator.OpenCreateUserDialog();

            Assert.Equal(1, dialog.CreateConfirmCallCount);
            Assert.Throws<ObjectDisposedException>(() => dialog.AttemptPasswords[0].Length);
            panelContext.Verify(c => c.RefreshAndNotifyDataChanged(It.IsAny<Guid?>(), It.IsAny<int>()), Times.Never);
            panelContext.Verify(c => c.SaveAndRefresh(It.IsAny<Guid?>(), It.IsAny<int>()), Times.Never);
            messageBoxService.Verify(
                m => m.Show(
                    It.IsAny<IWin32Window?>(),
                    It.Is<string>(text => text.Contains("commit exploded", StringComparison.Ordinal)),
                    "Account Creation Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1),
                Times.Once);
        });
    }

    [Fact]
    public void OpenCreateUserDialog_CleanupStateSaveFailed_ShowsWarningWithoutCommit()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            const string sid = "S-1-5-21-100-200-300-1001";
            var session = new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithClonedPinDerivedKey(_pinKey);

            var commitService = new Mock<IAccountCreationCommitService>(MockBehavior.Strict);
            var rollbackService = CreateRollbackService();
            using var dialog = new TestAccountCreationDialog(sid)
            {
                Attempts =
                [
                    new TestAccountCreationDialog.AttemptState(
                        FirewallSettingsChanged: false,
                        SettingsImportPath: null,
                        AllowInternet: true,
                        Status: CreateAccountStatus.CleanupStateSaveFailed,
                        ErrorMessage: "cleanup save failed",
                        IncludePassword: false)
                ]
            };
            var panelContext = new Mock<IAccountsPanelOperationContext>();
            panelContext.SetupGet(c => c.OwnerControl).Returns(new Panel());
            var messageBoxService = new Mock<IAccountMessageBoxService>();

            var orchestrator = CreateOrchestrator(
                session,
                commitService.Object,
                rollbackService,
                () => dialog,
                panelContext.Object,
                messageBoxService.Object);

            await orchestrator.OpenCreateUserDialog();

            commitService.Verify(
                s => s.Commit(It.IsAny<AccountCreationData>(), It.IsAny<AppDatabase>()),
                Times.Never);
            panelContext.Verify(c => c.RefreshAndNotifyDataChanged(null, -1), Times.Once);
            messageBoxService.Verify(
                m => m.Show(
                    It.IsAny<IWin32Window?>(),
                    It.Is<string>(text => text.Contains("could not save its cleanup state", StringComparison.Ordinal)),
                    "Account Created But Not Saved",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1),
                Times.Once);
        });
    }

    private static AccountCreationOrchestrator CreateOrchestrator(
        SessionContext session,
        IAccountCreationCommitService commitService,
        AccountCreationRollbackService rollbackService,
        Func<IAccountCreationDialog> dialogFactory,
        IAccountsPanelOperationContext panelContext,
        IAccountMessageBoxService messageBoxService)
    {
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        var evaluationLimitHelper = new Mock<IEvaluationLimitHelper>();
        evaluationLimitHelper.Setup(s => s.CheckCredentialLimit(
                It.IsAny<List<CredentialEntry>>(),
                It.IsAny<IWin32Window?>(),
                It.IsAny<string?>()))
            .Returns(true);

        var postCreateSetup = new AccountPostCreateSetupService(
            Mock.Of<ISettingsTransferService>(),
            new FirewallApplyHelper(
                Mock.Of<IAccountFirewallSettingsApplier>(),
                new DynamicPortRangeChecker(Mock.Of<ILoggingService>(), Mock.Of<IUserConfirmationService>(), new StandardNetshCommandRunner()),
                Mock.Of<ILoggingService>()),
            new PackageInstallService(
                Mock.Of<IPackageInstallLauncher>(),
                Mock.Of<IPackageInstallScriptStore>(),
                new AccountToolResolver(Mock.Of<IProfilePathResolver>()),
                Mock.Of<IWindowsTerminalAccountStateService>(),
                Mock.Of<IWindowsTerminalDeploymentService>()),
            sessionProvider.Object,
            new ImmediateAccountCreationProgressRunner(),
            Mock.Of<ILoggingService>());

        var launchService = new ToolLauncher(
            Mock.Of<ILaunchFacade>(),
            new AccountToolResolver(Mock.Of<IProfilePathResolver>()),
            Mock.Of<IWindowsTerminalAccountStateService>(),
            new TerminalLaunchIdentitySelector(Mock.Of<IDatabaseProvider>(), new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(Path.GetTempPath()))),
            new PackageInstallService(
                Mock.Of<IPackageInstallLauncher>(),
                Mock.Of<IPackageInstallScriptStore>(),
                new AccountToolResolver(Mock.Of<IProfilePathResolver>()),
                Mock.Of<IWindowsTerminalAccountStateService>(),
                Mock.Of<IWindowsTerminalDeploymentService>()),
            Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
            Mock.Of<IWindowsTerminalLaunchRefreshService>(),
            Mock.Of<ILaunchFeedbackPresenter>(),
            Mock.Of<ILoggingService>());

        var orchestrator = new AccountCreationOrchestrator(
            commitService,
            rollbackService,
            Mock.Of<IAccountLoginRestrictionService>(),
            sessionProvider.Object,
            dialogFactory,
            evaluationLimitHelper.Object,
            postCreateSetup,
            launchService,
            messageBoxService,
            Mock.Of<ILoggingService>());
        orchestrator.Initialize(panelContext);
        return orchestrator;
    }

    private static AccountCreationRollbackService CreateRollbackService(bool deleteSucceeded = true)
    {
        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(It.IsAny<string>()))
            .Returns<string>(sid => new AccountDeletionResult(deleteSucceeded, sid, deleteSucceeded ? null : "delete failed"));
        lifecycleManager.Setup(s => s.DeleteProfileAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        return new AccountCreationRollbackService(
            new CreatedAccountRollbackExecutor(
                lifecycleManager.Object,
                Mock.Of<IAccountCredentialManager>(),
                Mock.Of<IAssociationAutoSetService>(),
                Mock.Of<ILocalUserProvider>(),
                Mock.Of<ILoggingService>()));
    }

    private sealed class TestAccountCreationDialog : IAccountCreationDialog
    {
        private readonly List<ProtectedString> _attemptPasswords =
        [
            ProtectedString.FromChars("P@ssw0rd".AsSpan()),
            ProtectedString.FromChars("P@ssw0rd".AsSpan())
        ];
        private bool _passwordOwnershipTransferred;

        public TestAccountCreationDialog(string sid)
        {
            CreatedSid = sid;
            NewUsername = "newuser";
            SelectedPrivilegeLevel = PrivilegeLevel.Isolated;
            Errors = [];
            SelectedInstallPackages = Array.Empty<InstallablePackage>();
            Attempts =
            [
                new AttemptState(FirewallSettingsChanged: false, SettingsImportPath: null, AllowInternet: true),
                new AttemptState(FirewallSettingsChanged: false, SettingsImportPath: null, AllowInternet: true)
            ];
        }

        public event Func<Task<bool>>? CreateConfirmRequested;

        public sealed record AttemptState(
            bool FirewallSettingsChanged,
            string? SettingsImportPath,
            bool AllowInternet,
            CreateAccountStatus Status = CreateAccountStatus.Succeeded,
            string? ErrorMessage = null,
            bool IncludePassword = true);

        public int CreateConfirmCallCount { get; private set; }
        public IReadOnlyList<ProtectedString> AttemptPasswords => _attemptPasswords;
        public ProtectedString? LastTransferredPassword { get; private set; }
        public IReadOnlyList<AttemptState> Attempts { get; init; }
        public string? NewUsername { get; private set; }
        public bool IsEphemeral { get; set; }
        public string? SettingsImportPath { get; set; }
        public PrivilegeLevel SelectedPrivilegeLevel { get; set; }
        public bool AllowInternet { get; set; } = true;
        public bool AllowLocalhost { get; set; } = true;
        public bool AllowLan { get; set; } = true;
        public bool FirewallSettingsChanged { get; set; }
        public List<string> Errors { get; }
        public string? CreatedSid { get; private set; }
        public ProtectedString? CreatedPassword { get; private set; }
        public CreateAccountStatus CreatedAccountStatus { get; set; } = CreateAccountStatus.Succeeded;
        public string? CreatedAccountErrorMessage { get; set; }
        public bool UsersGroupUnchecked { get; set; }
        public bool AdminGroupChecked { get; set; }
        public IReadOnlyList<InstallablePackage> SelectedInstallPackages { get; set; }
        public CreatedAccountRollbackState? CreatedRollbackState { get; set; } = new()
        {
            Sid = "S-1-5-21-100-200-300-1001",
            Username = "newuser"
        };

        public void InitializeForCreate(
            string? prefillUsername = null,
            ProtectedString? prefillPassword = null,
            int currentHiddenCount = 0)
        {
            NewUsername = prefillUsername ?? NewUsername;
        }

        public async Task<DialogResult> ShowCreateDialogAsync(IWin32Window owner)
        {
            foreach (var pair in Attempts.Zip(_attemptPasswords, static (attempt, password) => (attempt, password)))
            {
                if (CreatedPassword != null && !ReferenceEquals(CreatedPassword, pair.password))
                    CreatedPassword.Dispose();

                FirewallSettingsChanged = pair.attempt.FirewallSettingsChanged;
                SettingsImportPath = pair.attempt.SettingsImportPath;
                AllowInternet = pair.attempt.AllowInternet;
                CreatedAccountStatus = pair.attempt.Status;
                CreatedAccountErrorMessage = pair.attempt.ErrorMessage;
                CreatedPassword = pair.attempt.IncludePassword ? pair.password : null;
                CreateConfirmCallCount++;
                if (await InvokeCreateConfirmRequested())
                {
                    LastTransferredPassword = CreatedPassword;
                    _passwordOwnershipTransferred = CreatedPassword != null;
                    return DialogResult.OK;
                }
            }

            return DialogResult.Cancel;
        }

        public void Dispose()
        {
            foreach (var password in _attemptPasswords)
            {
                if (_passwordOwnershipTransferred && ReferenceEquals(password, CreatedPassword))
                    continue;

                password.Dispose();
            }
        }

        private async Task<bool> InvokeCreateConfirmRequested()
        {
            var createConfirmRequested = CreateConfirmRequested;
            if (createConfirmRequested == null)
                return true;

            foreach (Func<Task<bool>> handler in createConfirmRequested.GetInvocationList())
            {
                if (!await handler())
                    return false;
            }

            return true;
        }
    }
}
