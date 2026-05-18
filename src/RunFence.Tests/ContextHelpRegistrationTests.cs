using Autofac;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.Forms;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core.Infrastructure;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge.UI.Forms;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.RunAs.UI;
using RunFence.RunAs.UI.Forms;
using RunFence.Persistence.UI.Forms;
using RunFence.Startup;
using RunFence.Tests.Helpers;
using RunFence.UI;
using RunFence.UI.Forms;
using Moq;
using System.Text.RegularExpressions;
using Xunit;

namespace RunFence.Tests;

public class ContextHelpRegistrationTests
{
    private static readonly string[] ContextHelpRegistrationSourceFiles =
    [
        @"Acl\UI\Forms\AclConfigSection.cs",
        @"Acl\UI\Forms\AclManagerDialog.cs",
        @"Account\UI\AppContainer\AppContainerEditDialog.cs",
        @"Account\UI\Forms\EditAccountDialog.cs",
        @"Apps\UI\Forms\AppEditDialog.cs",
        @"Apps\UI\Forms\CombinedPrefixesSection.cs",
        @"Apps\UI\Forms\HandlerMappingAddDialog.cs",
        @"Apps\UI\Forms\HandlerMappingsDialog.cs",
        @"DragBridge\UI\Forms\DragBridgeSection.cs",
        @"Firewall\UI\Forms\FirewallAllowlistDialog.cs",
        @"Persistence\UI\Forms\ConfigManagerSection.cs",
        @"RunAs\UI\Forms\RunAsDialog.cs",
        @"SidMigration\UI\Forms\MigrationMappingStep.cs",
        @"SidMigration\UI\Forms\MigrationProgressStep.cs",
        @"SidMigration\UI\Forms\SidMigrationInAppStep.cs",
        @"SidMigration\UI\Forms\SidMigrationPathStep.cs",
        @"SidMigration\UI\Forms\SidMigrationPreviewStep.cs",
        @"UI\Forms\IpcCallerSection.cs",
        @"UI\Forms\OptionsPanel.cs"
    ];

    private static readonly string[] DialogAndFormSourceFiles =
    [
        @"Acl\UI\Forms\AclManagerDialog.cs",
        @"Account\UI\AppContainer\AppContainerEditDialog.cs",
        @"Account\UI\Forms\EditAccountDialog.cs",
        @"Apps\UI\Forms\AppEditDialog.cs",
        @"Apps\UI\Forms\HandlerMappingAddDialog.cs",
        @"Apps\UI\Forms\HandlerMappingsDialog.cs",
        @"Firewall\UI\Forms\FirewallAllowlistDialog.cs",
        @"RunAs\UI\Forms\RunAsDialog.cs",
        @"SidMigration\UI\Forms\SidMigrationDialog.cs"
    ];

    [Fact]
    public void OptionsPanel_RegisterContextHelp_RegistersExpectedSectionTargets()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();
            using var pinKey = TestSecretFactory.Create(32);
            var session = new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithOwnedPinDerivedKey(pinKey);

            using var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(
                foundationContainer, session, new StartupOptions(false, false));

            using var panel = sessionScope.Resolve<OptionsPanel>();
            using var host = new ContextHelpForm();
            StaTestHelper.CreateControlTree(panel);
            panel.RegisterContextHelp(host);

            var autoStartCheckBox = FindControl<CheckBox>(panel, control => control.Text == "Auto-start on login");
            var callerSection = FindControl<IpcCallerSection>(panel, _ => true);

            Assert.DoesNotContain(panel, host.GetExplicitContextHelpControls());
            Assert.Contains(host.GetExplicitContextHelpControls(), c => host.TryGetContextHelp(c, out var t) && t == ContextHelpTextCatalog.Options_DragBridge);
            Assert.False(host.TryGetContextHelp(autoStartCheckBox, out _));
            AssertContextHelp(host, callerSection, ContextHelpTextCatalog.Launcher_LauncherAccessGlobal);
            Assert.NotNull(callerSection.Parent);
            Assert.False(host.TryGetContextHelp(callerSection.Parent, out _));
            Assert.Contains(host.GetExplicitContextHelpControls(), c => host.TryGetContextHelp(c, out var t) && t == ContextHelpTextCatalog.Launcher_LauncherAccessGlobal);
        });
    }

    [Fact]
    public void OptionsPanel_RegisterContextHelp_DoesNotRegisterRedundantMaintenanceButtonHelp()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();
            using var pinKey = TestSecretFactory.Create(32);
            var session = new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithOwnedPinDerivedKey(pinKey);

            using var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(
                foundationContainer, session, new StartupOptions(false, false));

            using var panel = sessionScope.Resolve<OptionsPanel>();
            using var host = new ContextHelpForm();
            StaTestHelper.CreateControlTree(panel);
            panel.RegisterContextHelp(host);

            var controlsGroup = FindControl<GroupBox>(panel, control => control.Text == "Controls");
            var cleanupButton = FindControl<Button>(panel, control => control.Text == "Cleanup && Exit");
            var migrateButton = FindControl<Button>(panel, control => control.Text == "Migrate To");

            Assert.False(host.TryGetContextHelp(controlsGroup, out _));
            Assert.False(host.TryGetContextHelp(cleanupButton, out _));
            Assert.False(host.TryGetContextHelp(migrateButton, out _));
        });
    }

    [Fact]
    public void AppEditDialog_RegisterContextHelp_DoesNotRegisterFormItself()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var dialog = AppEditDialogTestsAccessor.CreateDialogForContextHelp();
            dialog.Initialize(
                existing: null,
                credentials: [],
                existingApps: [],
                commandContext: AppEditDialogTestsAccessor.CreateNoOpCommandContext());

            Assert.DoesNotContain(dialog, dialog.GetExplicitContextHelpControls());
            Assert.Contains(dialog.GetExplicitContextHelpControls(), c => dialog.TryGetContextHelp(c, out var t) && t == ContextHelpTextCatalog.App_PathPrefixes);
            Assert.Contains(dialog.GetExplicitContextHelpControls(), c => dialog.TryGetContextHelp(c, out var t) && t == ContextHelpTextCatalog.AppEdit_LauncherAccessOverride);
            Assert.Contains(dialog.GetExplicitContextHelpControls(), c => dialog.TryGetContextHelp(c, out var t) && t == ContextHelpTextCatalog.App_EnvironmentVariables);
        });
    }

    [Fact]
    public void SidMigrationPathStep_DoesNotExposeRedundantPathActionButtonHelp()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var step = new RunFence.SidMigration.UI.Forms.SidMigrationPathStep(showSkipButton: true);
            using var host = new ContextHelpForm();

            var selectAllButton = FindControl<Button>(step, control => control.Text == "Select All");
            var deselectAllButton = FindControl<Button>(step, control => control.Text == "Deselect All");
            var addPathButton = FindControl<Button>(step, control => control.Text == "Add Path...");
            var skipButton = FindControl<Button>(step, control => control.Text == "Skip — I know the SIDs");

            Assert.False(host.TryGetContextHelp(selectAllButton, out _));
            Assert.False(host.TryGetContextHelp(deselectAllButton, out _));
            Assert.False(host.TryGetContextHelp(addPathButton, out _));
            Assert.False(host.TryGetContextHelp(skipButton, out _));
        });
    }

    [Fact]
    public void SharedConcepts_ReuseSameCatalogTextAcrossMultipleTargets()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();
            using var pinKey = TestSecretFactory.Create(32);
            var session = new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithOwnedPinDerivedKey(pinKey);

            using var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(
                foundationContainer, session, new StartupOptions(false, false));

            using var appEditDialog = AppEditDialogTestsAccessor.CreateDialogForContextHelp();
            appEditDialog.Initialize(
                existing: null,
                credentials: [],
                existingApps: [],
                commandContext: AppEditDialogTestsAccessor.CreateNoOpCommandContext());
            using var runAsDialog = sessionScope.Resolve<RunAsDialog>();
            runAsDialog.Initialize(new RunAsDialogOptions(
                FilePath: @"C:\Apps\Test.exe",
                Arguments: "--flag",
                Credentials: [new CredentialEntry { Sid = "S-1-5-21-100" }],
                ExistingApps: [],
                CurrentUserSid: "S-1-5-21-999",
                SidNames: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["S-1-5-21-100"] = "TestUser"
                }));

            Assert.Contains(appEditDialog.GetExplicitContextHelpControls(),
                control => appEditDialog.TryGetContextHelp(control, out var text) && text == ContextHelpTextCatalog.Launch_Arguments);
            Assert.Contains(runAsDialog.GetExplicitContextHelpControls(),
                control => runAsDialog.TryGetContextHelp(control, out var text) && text == ContextHelpTextCatalog.Launch_Arguments);

            Assert.Contains(runAsDialog.GetExplicitContextHelpControls(),
                control => runAsDialog.TryGetContextHelp(control, out var text) && text == ContextHelpTextCatalog.Launch_PrivilegeLevel);
        });
    }

    [Fact]
    public void ReusableSections_RegisterOnlyTheirOwnTargets()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new ContextHelpForm();
            using var aclSection = CreateAclSection();
            using var dragBridgeSection = new DragBridgeSection();
            using var configSection = CreateConfigManagerSection();
            using var callerSection = CreateIpcCallerSection();
            using var pathPrefixesSection = new PathPrefixesSection();
            using var combinedPrefixesSection = new CombinedPrefixesSection();

            aclSection.RegisterContextHelp(host);
            dragBridgeSection.RegisterContextHelp(host);
            configSection.RegisterContextHelp(host);
            callerSection.RegisterContextHelp(host, ContextHelpTextCatalog.Launcher_LauncherAccessGlobal);
            pathPrefixesSection.RegisterContextHelp(host);
            combinedPrefixesSection.RegisterContextHelp(host);

            foreach (var control in host.GetExplicitContextHelpControls())
                Assert.True(IsDescendantOfAny(control, aclSection, dragBridgeSection, configSection, callerSection, pathPrefixesSection, combinedPrefixesSection));

            var callerToolStrip = FindControl<ToolStrip>(callerSection, _ => true);
            var pathPrefixesGroup = FindControl<GroupBox>(pathPrefixesSection, _ => true);
            var pathPrefixesGrid = FindControl<DataGridView>(pathPrefixesSection, _ => true);
            var combinedPrefixesGroup = FindControl<GroupBox>(combinedPrefixesSection, _ => true);
            var combinedPrefixesGrid = FindControl<DataGridView>(combinedPrefixesSection, _ => true);
            AssertContextHelp(host, pathPrefixesGroup, ContextHelpTextCatalog.App_PathPrefixes);
            Assert.False(host.TryGetContextHelp(pathPrefixesGrid, out _));
            AssertContextHelp(host, combinedPrefixesGroup, ContextHelpTextCatalog.App_PathPrefixes);
            Assert.False(host.TryGetContextHelp(combinedPrefixesGrid, out _));
            Assert.DoesNotContain(host.GetExplicitContextHelpControls().OfType<RadioButton>(),
                radioButton => host.TryGetContextHelp(radioButton, out var text) && text == ContextHelpTextCatalog.App_PathPrefixes);
            AssertContextHelp(host, callerSection, ContextHelpTextCatalog.Launcher_LauncherAccessGlobal);
            Assert.False(host.TryGetContextHelp(callerToolStrip, out _));
            Assert.Empty(host.GetExplicitContextHelpToolStripDropDowns());
            Assert.Empty(host.GetExplicitContextHelpToolStripItems());
        });
    }

    [Fact]
    public void DialogForms_DoNotRegisterContextHelpOnThemselves()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var notifier = Mock.Of<RunFence.Account.UI.AppContainer.IAppContainerEditDialogNotifier>();
            using var dialog = AppEditDialogTestsAccessor.CreateDialogForContextHelp();
            dialog.Initialize(
                existing: null,
                credentials: [],
                existingApps: [],
                commandContext: AppEditDialogTestsAccessor.CreateNoOpCommandContext());

            using var containerDialog = new RunFence.Account.UI.AppContainer.AppContainerEditDialog(
                submitController: new RunFence.Account.UI.AppContainer.AppContainerEditSubmitController(new StubAppContainerEditService()),
                stateAssembler: new RunFence.Account.UI.AppContainer.AppContainerDialogStateAssembler(),
                capabilitiesBinder: new RunFence.Account.UI.AppContainer.AppContainerCapabilitiesBinder(notifier),
                resultPresenter: new RunFence.Account.UI.AppContainer.AppContainerDialogResultPresenter(notifier));
            containerDialog.Initialize(existing: null);
            StaTestHelper.CreateControlTree(containerDialog);

            Assert.DoesNotContain(dialog, dialog.GetExplicitContextHelpControls());
            Assert.DoesNotContain(containerDialog, containerDialog.GetExplicitContextHelpControls());
        });
    }

    [Fact]
    public void ContextHelpForm_TracksExactSecurePasswordBoxInstance()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new ContextHelpForm();
            using var passwordTextBox = new TextBox();
            host.Controls.Add(passwordTextBox);
            using var securePasswordBox = new SecurePasswordBox(passwordTextBox);
            StaTestHelper.CreateControlTree(host);

            Assert.Contains(
                host.GetContextHelpSnapshotParticipants(),
                participant => ReferenceEquals(participant, securePasswordBox));
        });
    }

    [Fact]
    public void ContextHelpRegistrationSources_DoNotUseInlineHelpStringLiterals()
    {
        foreach (var relativePath in ContextHelpRegistrationSourceFiles)
        {
            var source = File.ReadAllText(GetRunFenceSourcePath(relativePath));

            Assert.DoesNotMatch(
                new Regex("SetContextHelp\\s*\\([^,]+,\\s*\"", RegexOptions.CultureInvariant),
                source);
        }
    }

    [Fact]
    public void DialogAndFormSources_DoNotRegisterContextHelpOnThemselves()
    {
        foreach (var relativePath in DialogAndFormSourceFiles)
        {
            var source = File.ReadAllText(GetRunFenceSourcePath(relativePath));

            Assert.DoesNotMatch(
                new Regex(@"SetContextHelp\s*\(\s*this\s*,", RegexOptions.CultureInvariant),
                source);
        }
    }

    private static AclConfigSection CreateAclSection()
    {
        var sidResolver = new Mock<ISidResolver>();
        sidResolver.Setup(s => s.TryResolveName(It.IsAny<string>())).Returns<string>(sid => sid);
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var displayNameResolver = new SidDisplayNameResolver(sidResolver.Object, profilePathResolver.Object);
        var aclService = new Mock<IAclService>();
        aclService.Setup(s => s.IsBlockedPath(It.IsAny<string>())).Returns(false);

        return new AclConfigSection(
            new AclAllowListGridHandler(),
            new AllowListEntryFactory(
                Mock.Of<ILocalUserProvider>(),
                Mock.Of<ILocalGroupMembershipService>(),
                Mock.Of<ISidEntryHelper>(),
                displayNameResolver),
            new AclConfigValidator(aclService.Object, Mock.Of<ILoggingService>()),
            new FolderDepthHelper(aclService.Object, Mock.Of<ILoggingService>()));
    }

    private static ConfigManagerSection CreateConfigManagerSection()
    {
        var pinKey = TestSecretFactory.Create(32);
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(pinKey));

        return new ConfigManagerSection(
            Mock.Of<IAppConfigService>(),
            Mock.Of<IAppFilter>(),
            Mock.Of<ILoggingService>(),
            Mock.Of<IAclPermissionService>(),
            null!,
            null!,
            sessionProvider.Object,
            Mock.Of<IAccountSidResolutionService>(),
            null!,
            Mock.Of<IMessageBoxService>());
    }

    private static IpcCallerSection CreateIpcCallerSection()
    {
        var sidResolver = new Mock<ISidResolver>();
        sidResolver.Setup(s => s.TryResolveName(It.IsAny<string>())).Returns<string>(sid => sid);
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var displayNameResolver = new SidDisplayNameResolver(sidResolver.Object, profilePathResolver.Object);

        return new IpcCallerSection(
            () => [],
            Mock.Of<ISidEntryHelper>(),
            displayNameResolver);
    }

    private static void AssertContextHelp(ContextHelpForm host, Control control, string expectedText)
    {
        Assert.True(host.TryGetContextHelp(control, out var actualText));
        Assert.Equal(expectedText, actualText);
    }

    private static T FindControl<T>(Control root, Func<T, bool> predicate)
        where T : Control
        => EnumerateControls(root).OfType<T>().First(predicate);

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;

        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (var nested in EnumerateControls(child))
                yield return nested;
        }
    }

    private static bool IsDescendantOfAny(Control control, params Control[] roots)
        => roots.Any(root =>
        {
            for (var current = control; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, root))
                    return true;
            }

            return false;
        });

    private static string GetRunFenceSourcePath(string relativePath)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RunFence",
            relativePath));

    private sealed class StubAppContainerEditService : RunFence.Account.UI.AppContainer.IAppContainerEditService
    {
        public Task<RunFence.Account.UI.AppContainer.AppContainerEditResult> ApplyEditChanges(
            RunFence.Core.Models.AppContainerEntry existing,
            string displayName,
            List<string> capabilities,
            bool loopback,
            List<string> newComClsids,
            bool isEphemeral)
            => Task.FromResult(new RunFence.Account.UI.AppContainer.AppContainerEditResult(
                RunFence.Account.UI.AppContainer.AppContainerOperationStatus.Succeeded,
                existing,
                false,
                null,
                []));

        public Task<RunFence.Account.UI.AppContainer.AppContainerCreateResult> CreateNewContainer(
            string profileName,
            string displayName,
            bool isEphemeral,
            List<string> capabilities,
            bool loopback,
            List<string> comClsids)
            => Task.FromResult(new RunFence.Account.UI.AppContainer.AppContainerCreateResult(
                RunFence.Account.UI.AppContainer.AppContainerOperationStatus.Succeeded,
                new RunFence.Core.Models.AppContainerEntry { Name = profileName, DisplayName = displayName },
                null,
                []));
    }
}
