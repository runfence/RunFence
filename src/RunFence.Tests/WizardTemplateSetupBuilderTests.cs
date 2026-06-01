using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launching.Resolution;
using RunFence.Persistence;
using RunFence.Firewall.UI;
using RunFence.PrefTrans;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Tests.Helpers;
using RunFence.UI;
using RunFence.Wizard;
using RunFence.Wizard.Templates;
using Xunit;

namespace RunFence.Tests;

public class WizardTemplateSetupBuilderTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private const string DesktopSettingsPath = @"C:\desktop.rfn";

    [Fact]
    public void GamingAccountTemplateState_Reset_DisposesSecretsAndClearsCollectedData()
    {
        var collectedPassword = ProtectedString.FromChars("Collected1!".AsSpan());
        var password = ProtectedString.FromChars("WizardPass1!".AsSpan());
        var state = new GamingAccountTemplateState
        {
            IsExistingAccount = true,
            ExistingAccountSid = Sid,
            CollectedPassword = collectedPassword,
            Username = "gamer",
            Password = password,
            GameFolders = [@"C:\Games"],
            GameLaunchers = [@"C:\Games\Launcher.exe"]
        };

        state.Reset();

        Assert.False(state.IsExistingAccount);
        Assert.Null(state.ExistingAccountSid);
        Assert.Equal(string.Empty, state.Username);
        Assert.Null(state.CollectedPassword);
        Assert.Null(state.Password);
        Assert.Empty(state.GameFolders);
        Assert.Empty(state.GameLaunchers);
        Assert.Throws<ObjectDisposedException>(() => collectedPassword.Length);
        Assert.Throws<ObjectDisposedException>(() => password.Length);
    }

    [Fact]
    public void GamingAccountTemplateState_DisposeSecrets_DisposesPasswordsWithoutResettingOtherState()
    {
        var collectedPassword = ProtectedString.FromChars("Collected1!".AsSpan());
        var password = ProtectedString.FromChars("WizardPass1!".AsSpan());
        var state = new GamingAccountTemplateState
        {
            IsExistingAccount = true,
            ExistingAccountSid = Sid,
            CollectedPassword = collectedPassword,
            Username = "gamer",
            Password = password,
            GameFolders = [@"C:\Games"],
            GameLaunchers = [@"C:\Games\Launcher.exe"]
        };

        state.DisposeSecrets();

        Assert.True(state.IsExistingAccount);
        Assert.Equal(Sid, state.ExistingAccountSid);
        Assert.Equal("gamer", state.Username);
        Assert.Null(state.CollectedPassword);
        Assert.Null(state.Password);
        Assert.Equal([@"C:\Games"], state.GameFolders);
        Assert.Equal([@"C:\Games\Launcher.exe"], state.GameLaunchers);
        Assert.Throws<ObjectDisposedException>(() => collectedPassword.Length);
        Assert.Throws<ObjectDisposedException>(() => password.Length);
    }

    [Fact]
    public async Task BuildGamingNewAccountFlow_MapsRequestSetupAndPreEnforcementBehavior()
    {
        using var session = CreateSession();
        var pathGrantService = new Mock<IGrantMutatorService>();
        pathGrantService
            .Setup(service => service.EnsureAccess(
                Sid,
                @"C:\Games\NewFolder",
                It.Is<SavedRightsState>(rights => rights.Execute && rights.Write && rights.Read && rights.Special && rights.Own),
                null,
                false))
            .Returns(new GrantApplyResult(GrantApplied: true));
        var grantHelper = new WizardFolderGrantHelper(pathGrantService.Object, Mock.Of<IQuickAccessPinService>());
        var builder = CreateBuilder(session, grantHelper, Mock.Of<IWindowsAccountQueryService>());
        var state = new GamingAccountTemplateState
        {
            Username = "gamer",
            Password = ProtectedString.FromChars("WizardPass1!".AsSpan()),
            GameFolders = [@"C:\Games\NewFolder"],
            GameLaunchers = [@"D:\Games\Launcher.exe"]
        };

        try
        {
            var flow = builder.BuildGamingNewAccountFlow(state, Mock.Of<IWizardProgressReporter>());
            var buildOptions = flow.BuildOptionsFactory!(Sid);
            await flow.PreEnforcementAction!(session, Sid);

            Assert.NotNull(flow.Request);
            Assert.Equal("gamer", flow.Request!.Username);
            Assert.True(flow.Request.AllowLogon);
            Assert.False(flow.Request.AllowNetworkLogin);
            Assert.False(flow.Request.AllowBgAutorun);
            Assert.Equal([(GroupFilterHelper.UsersSid, "Users")], flow.Request.CheckedGroups);
            Assert.Empty(flow.Request.UncheckedGroups);
            Assert.NotNull(flow.SetupOptions);
            Assert.True(flow.SetupOptions!.StoreCredential);
            Assert.False(flow.SetupOptions.IsEphemeral);
            Assert.Equal(PrivilegeLevel.Isolated, flow.SetupOptions.PrivilegeLevel);
            Assert.NotNull(flow.SetupOptions.FirewallSettings);
            Assert.False(flow.SetupOptions.FirewallSettings!.AllowLan);
            Assert.False(flow.SetupOptions.FirewallSettings.AllowLocalhost);
            Assert.Equal(DesktopSettingsPath, flow.SetupOptions.DesktopSettingsPath);
            Assert.Null(flow.SetupOptions.InstallPackages);
            Assert.False(flow.SetupOptions.TrayTerminal);
            Assert.True(flow.CreateDesktopShortcut);
            Assert.Collection(
                buildOptions!,
                option =>
                {
                    Assert.Equal(@"D:\Games\Launcher.exe", option.ExePath);
                    Assert.True(option.RestrictAcl);
                    Assert.Equal(AclMode.Deny, option.AclMode);
                    Assert.Equal(AclTarget.Folder, option.AclTarget);
                });
            pathGrantService.VerifyAll();
            flow.Request.Password.Dispose();
            flow.Request.ConfirmPassword.Dispose();
        }
        finally
        {
            state.Password?.Dispose();
        }
    }

    [Fact]
    public async Task BuildGamingExistingAccountFlow_FiltersExistingGrantsAndExistingLaunchers()
    {
        using var session = CreateSession();
        session.Database.Accounts.Add(new AccountEntry
        {
            Sid = Sid,
            Grants =
            [
                new GrantedPathEntry
                {
                    Path = @"C:\Games\ExistingFolder"
                }
            ]
        });
        session.Database.Apps.Add(new AppEntry
        {
            AccountSid = Sid,
            ExePath = @"C:\Games\ExistingLauncher.exe"
        });

        var pathGrantService = new Mock<IGrantMutatorService>();
        pathGrantService
            .Setup(service => service.EnsureAccess(
                Sid,
                @"C:\Games\NewFolder",
                It.Is<SavedRightsState>(rights => rights.Execute && rights.Write && rights.Read && rights.Special && rights.Own),
                null,
                false))
            .Returns(new GrantApplyResult(GrantApplied: true));
        var grantHelper = new WizardFolderGrantHelper(pathGrantService.Object, Mock.Of<IQuickAccessPinService>());

        var queryService = new Mock<IWindowsAccountQueryService>();
        queryService
            .Setup(service => service.GetProfilePath(Sid))
            .Returns(new AccountQueryResult(
                AccountQueryStatus.Succeeded,
                null,
                @"C:\Users\gamer",
                null,
                null,
                null));

        var builder = CreateBuilder(session, grantHelper, queryService.Object);
        var state = new GamingAccountTemplateState
        {
            IsExistingAccount = true,
            ExistingAccountSid = Sid,
            GameFolders = [@"C:\Games\ExistingFolder", @"C:\Games\NewFolder"],
            GameLaunchers = [@"C:\Games\ExistingLauncher.exe", @"C:\Users\gamer\LocalLauncher.exe", @"D:\Games\SharedLauncher.exe"]
        };

        var flow = builder.BuildGamingExistingAccountFlow(state, Mock.Of<IWizardProgressReporter>());
        var buildOptions = flow.BuildOptionsFactory!(Sid);
        await flow.PreEnforcementAction!(session, Sid);

        Assert.Null(flow.Request);
        Assert.Null(flow.SetupOptions);
        Assert.Equal(Sid, flow.AccountSid);
        Assert.True(flow.CreateDesktopShortcut);
        Assert.Collection(
            buildOptions!,
            localLauncher =>
            {
                Assert.Equal(@"C:\Users\gamer\LocalLauncher.exe", localLauncher.ExePath);
                Assert.False(localLauncher.RestrictAcl);
                Assert.Equal(AclTarget.Folder, localLauncher.AclTarget);
            },
            sharedLauncher =>
            {
                Assert.Equal(@"D:\Games\SharedLauncher.exe", sharedLauncher.ExePath);
                Assert.True(sharedLauncher.RestrictAcl);
                Assert.Equal(AclTarget.Folder, sharedLauncher.AclTarget);
            });
        pathGrantService.VerifyAll();
    }

    [Fact]
    public async Task BuildAiAgentFlow_WithPackagedClaudeCode_MapsPackageInstallAndNoAppEntry()
    {
        using var session = CreateSession();
        var pathGrantService = new Mock<IGrantMutatorService>();
        pathGrantService
            .Setup(service => service.EnsureAccess(
                Sid,
                @"C:\Projects\Repo",
                It.Is<SavedRightsState>(rights => !rights.Execute && rights.Write && rights.Read && !rights.Special && !rights.Own),
                null,
                false))
            .Returns(new GrantApplyResult(GrantApplied: true));
        var grantHelper = new WizardFolderGrantHelper(pathGrantService.Object, Mock.Of<IQuickAccessPinService>());

        var executableKindService = new Mock<IExecutableKindService>();
        executableKindService
            .Setup(service => service.IsUwpExeFile(@"C:\Tools\Agent.exe"))
            .Returns(true);

        var builder = CreateBuilder(
            session,
            grantHelper,
            Mock.Of<IWindowsAccountQueryService>(),
            executableKindService.Object);
        var state = new AiAgentTemplateState
        {
            Username = "agent",
            UseAiPackage = true,
            ProjectPaths = [@"C:\Projects\Repo"],
            AllowInternet = false,
            AllowLan = true,
            AllowLocalhost = true
        };

        var flow = builder.BuildAiAgentFlow(state, Mock.Of<IWizardProgressReporter>());
        var buildOptions = flow.BuildOptionsFactory!(Sid);
        await flow.PreEnforcementAction!(session, Sid);

        Assert.NotNull(flow.Request);
        Assert.Equal("agent", flow.Request!.Username);
        Assert.NotNull(flow.SetupOptions);
        Assert.Equal(DesktopSettingsPath, flow.SetupOptions!.DesktopSettingsPath);
        Assert.False(flow.SetupOptions.FirewallSettings!.AllowInternet);
        Assert.True(flow.SetupOptions.FirewallSettings.AllowLan);
        Assert.True(flow.SetupOptions.FirewallSettings.AllowLocalhost);
        Assert.Equal([KnownPackages.WindowsTerminal, KnownPackages.ClaudeCode], flow.SetupOptions.InstallPackages);
        Assert.True(flow.SetupOptions.TrayTerminal);
        Assert.True(flow.SetupOptions.WaitForInstallPackages);
        Assert.False(flow.CreateDesktopShortcut);
        Assert.Equal(Sid, state.CreatedSid);
        Assert.Empty(buildOptions!);
        pathGrantService.VerifyAll();
    }

    [Fact]
    public async Task BuildAiAgentFlow_WithCustomExecutable_MapsAppBuildOptionsWithoutClaudeCodePackage()
    {
        using var session = CreateSession();
        var pathGrantService = new Mock<IGrantMutatorService>();
        pathGrantService
            .Setup(service => service.EnsureAccess(
                Sid,
                @"C:\Projects\Repo",
                It.Is<SavedRightsState>(rights => !rights.Execute && rights.Write && rights.Read && !rights.Special && !rights.Own),
                null,
                false))
            .Returns(new GrantApplyResult(GrantApplied: true));
        var grantHelper = new WizardFolderGrantHelper(pathGrantService.Object, Mock.Of<IQuickAccessPinService>());

        var executableKindService = new Mock<IExecutableKindService>();
        executableKindService
            .Setup(service => service.IsUwpExeFile(@"C:\Tools\Agent.exe"))
            .Returns(true);

        var builder = CreateBuilder(
            session,
            grantHelper,
            Mock.Of<IWindowsAccountQueryService>(),
            executableKindService.Object);
        var state = new AiAgentTemplateState
        {
            Username = "agent",
            UseAiPackage = false,
            ProjectPaths = [@"C:\Projects\Repo"],
            AppPath = @"C:\Tools\Agent.exe",
            AllowInternet = false,
            AllowLan = true,
            AllowLocalhost = true
        };

        var flow = builder.BuildAiAgentFlow(state, Mock.Of<IWizardProgressReporter>());
        var buildOptions = flow.BuildOptionsFactory!(Sid);
        await flow.PreEnforcementAction!(session, Sid);

        Assert.NotNull(flow.Request);
        Assert.NotNull(flow.SetupOptions);
        Assert.Equal([KnownPackages.WindowsTerminal], flow.SetupOptions!.InstallPackages);
        Assert.True(flow.CreateDesktopShortcut);
        Assert.Equal(Sid, state.CreatedSid);
        Assert.Collection(
            buildOptions!,
            option =>
            {
                Assert.Equal(@"C:\Tools\Agent.exe", option.ExePath);
                Assert.False(option.RestrictAcl);
                Assert.Equal(PrivilegeLevel.Basic, option.PrivilegeLevel);
            });
        pathGrantService.VerifyAll();
    }

    [Fact]
    public void BuildAiAgentFlow_WithHighIntegrityAccount_KeepsAccountDefaultInsteadOfDowngradingToBasic()
    {
        using var session = CreateSession();
        session.Database.GetOrCreateAccount(Sid).PrivilegeLevel = PrivilegeLevel.HighIntegrity;

        var executableKindService = new Mock<IExecutableKindService>();
        executableKindService
            .Setup(service => service.IsUwpExeFile(@"C:\Tools\Agent.exe"))
            .Returns(true);

        var builder = CreateBuilder(
            session,
            new WizardFolderGrantHelper(Mock.Of<IGrantMutatorService>(), Mock.Of<IQuickAccessPinService>()),
            Mock.Of<IWindowsAccountQueryService>(),
            executableKindService.Object);
        var state = new AiAgentTemplateState
        {
            Username = "agent",
            ProjectPaths = [],
            AppPath = @"C:\Tools\Agent.exe"
        };

        var flow = builder.BuildAiAgentFlow(state, Mock.Of<IWizardProgressReporter>());
        var buildOptions = flow.BuildOptionsFactory!(Sid);

        Assert.Collection(
            buildOptions!,
            option => Assert.Null(option.PrivilegeLevel));
    }

    private static WizardTemplateSetupBuilder CreateBuilder(
        SessionContext session,
        WizardFolderGrantHelper folderGrantHelper,
        IWindowsAccountQueryService windowsAccountQueryService,
        IExecutableKindService? executableKindService = null)
    {
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider
            .Setup(provider => provider.GetDatabase())
            .Returns(session.Database);
        session.Database.Settings.DefaultDesktopSettingsPath = DesktopSettingsPath;
        var setupHelperFactory = new WizardAccountSetupHelperFactory(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            databaseProvider.Object,
            null!);

        return new WizardTemplateSetupBuilder(
            setupHelperFactory,
            folderGrantHelper,
            session,
            windowsAccountQueryService,
            executableKindService ?? Mock.Of<IExecutableKindService>());
    }

    private static SessionContext CreateSession() =>
        new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
}
