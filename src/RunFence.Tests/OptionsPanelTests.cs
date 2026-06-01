using Autofac;
using Moq;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge.UI.Forms;
using RunFence.ForegroundMarker;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Persistence.UI.Forms;
using RunFence.Security;
using RunFence.Startup;
using RunFence.Tests.Helpers;
using RunFence.Tests.TestHelpers;
using RunFence.UI;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class OptionsPanelTests
{
    private const string ForegroundMarkerWarningText =
        "Your preference was saved, but the foreground marker is unavailable for the rest of this RunFence session. Restart RunFence to apply the saved setting.";

    [Fact]
    public void RememberPinToggle_RaisesPinDerivedKeyChanged_AndKeepsCheckboxInSync()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreatePanelContext();
            context.AutoStartService.Setup(service => service.IsAutoStartEnabled()).ReturnsAsync(false);
            context.SetRememberPinEnabled(false);

            using var panel = context.Scope.Resolve<OptionsPanel>();
            StaTestHelper.CreateControlTree(panel);
            panel.SetData(context.Session);
            StaTestHelper.PumpUntil(() => !FindCheckBox(panel, "Remember PIN").Checked);

            var callbackCount = 0;
            var keyBeforeToggle = context.Session.PinDerivedKey;
            panel.PinDerivedKeyChanged += () => callbackCount++;

            FindCheckBox(panel, "Remember PIN").Checked = true;
            Application.DoEvents();

            Assert.True(context.RememberPinEnabled);
            Assert.True(FindCheckBox(panel, "Remember PIN").Checked);
            Assert.Equal(1, callbackCount);
            Assert.NotSame(context.InitialCurrentPinKey, context.Session.PinDerivedKey);
            Assert.NotSame(keyBeforeToggle, context.Session.PinDerivedKey);
            Assert.False(context.Session.PinDerivedKey is SecureSecret);
            Assert.NotSame(context.InitialPinKey, context.Session.PinDerivedKey);
        });
    }

    [Fact]
    public void RegisterContextHelp_WiresPanelAndDelegatedSections()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreatePanelContext();
            using var panel = context.Scope.Resolve<OptionsPanel>();
            using var host = new ContextHelpForm();
            StaTestHelper.CreateControlTree(panel);
            panel.RegisterContextHelp(host);
            var callerSection = FindControls<IpcCallerSection>(panel).Single();
            var callerGroup = FindControl<Panel>(panel, control => control.Controls.OfType<IpcCallerSection>().Any());

            var firewallGroup = FindControl<GroupBox>(panel, control => control.Text == "Firewall");
            var folderBrowserGroup = FindControl<GroupBox>(panel, control => control.Text == "Folder Browser");
            var desktopSettingsGroup = FindControl<GroupBox>(panel, control => control.Text == "Desktop Settings");
            var dragBridgeGroup = FindControl<GroupBox>(panel, control => control.Text == "Drag Bridge (Cross-Account Drag & Drop)");

            AssertHelp(host, firewallGroup, ContextHelpTextCatalog.Options_FirewallIcmp);
            AssertHelp(host, folderBrowserGroup, ContextHelpTextCatalog.Options_FolderBrowser);
            AssertHelp(host, desktopSettingsGroup, ContextHelpTextCatalog.Options_DesktopSettingsTransfer);
            AssertHelp(host, dragBridgeGroup, ContextHelpTextCatalog.Options_DragBridge);
            Assert.False(host.TryGetContextHelp(FindCheckBox(panel, "Lock in background"), out _));
            NoDefaultContextHelpForDescendants(folderBrowserGroup, host, ContextHelpTextCatalog.Options_FolderBrowser);
            NoDefaultContextHelpForDescendants(desktopSettingsGroup, host, ContextHelpTextCatalog.Options_DesktopSettingsTransfer);
            NoDefaultContextHelpForDescendants(dragBridgeGroup, host, ContextHelpTextCatalog.Options_DragBridge);
            AssertHelp(host, callerSection, ContextHelpTextCatalog.Launcher_LauncherAccessGlobal);
            NoDefaultContextHelpForDescendants(callerSection, host, ContextHelpTextCatalog.Launcher_LauncherAccessGlobal);
            Assert.False(host.TryGetContextHelp(callerGroup, out _));
        });
    }

    [Fact]
    public void ForegroundPrivilegeMarkerToggle_DisablesFullscreenCheckboxWhenUnchecked()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreatePanelContext();
            context.Session.Database.Settings.ShowForegroundPrivilegeMarker = true;
            context.Session.Database.Settings.ShowForegroundPrivilegeMarkerWhenFullscreen = true;
            context.AutoStartService.Setup(service => service.IsAutoStartEnabled()).ReturnsAsync(false);

            using var panel = context.Scope.Resolve<OptionsPanel>();
            StaTestHelper.CreateControlTree(panel);
            panel.SetData(context.Session);
            StaTestHelper.PumpUntil(() => FindForegroundPrivilegeMarkerCheckBox(panel).Checked);

            FindForegroundPrivilegeMarkerCheckBox(panel).Checked = false;
            Application.DoEvents();

            Assert.False(FindFullscreenForegroundPrivilegeMarkerCheckBox(panel).Enabled);
            Assert.True(FindFullscreenForegroundPrivilegeMarkerCheckBox(panel).Checked);
        });
    }

    [Fact]
    public void ForegroundPrivilegeMarkerToggle_Enabling_SavesBeforeStartingMarkerRuntime()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreatePanelContext();
            context.Session.Database.Settings.ShowForegroundPrivilegeMarker = false;
            context.AutoStartService.Setup(service => service.IsAutoStartEnabled()).ReturnsAsync(false);

            var persistence = new Mock<IMainConfigPersistence>(MockBehavior.Strict);
            var markerService = new Mock<IForegroundPrivilegeMarkerService>(MockBehavior.Strict);
            var sequence = new MockSequence();
            persistence.InSequence(sequence)
                .Setup(service => service.SaveConfig(
                    It.IsAny<AppDatabase>(),
                    It.IsAny<ISecureSecretSnapshotSource>(),
                    It.IsAny<byte[]>()));
            markerService.InSequence(sequence)
                .Setup(service => service.SetMarkerWindowEnabled(true));

            using var overrideScope = context.Scope.BeginLifetimeScope(builder =>
            {
                builder.RegisterInstance(persistence.Object).As<IMainConfigPersistence>();
                builder.RegisterInstance(markerService.Object).As<IForegroundPrivilegeMarkerService>();
                builder.RegisterType<OptionsSettingsHandler>().AsSelf().SingleInstance();
                builder.RegisterType<OptionsForegroundPrivilegeMarkerSection>().AsSelf().SingleInstance();
                builder.RegisterType<OptionsPanel>().AsSelf().InstancePerDependency();
            });

            using var panel = overrideScope.Resolve<OptionsPanel>();
            StaTestHelper.CreateControlTree(panel);
            panel.SetData(context.Session);
            StaTestHelper.PumpUntil(() => !FindForegroundPrivilegeMarkerCheckBox(panel).Checked);

            FindForegroundPrivilegeMarkerCheckBox(panel).Checked = true;
            Application.DoEvents();

            Assert.True(context.Session.Database.Settings.ShowForegroundPrivilegeMarker);
            persistence.VerifyAll();
            markerService.Verify(service => service.SetMarkerWindowEnabled(true), Times.Once);
        });
    }

    [Fact]
    public void ForegroundPrivilegeMarkerToggle_Disabling_SavesPreferenceBeforeApplyingMarkerWindowState()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreatePanelContext();
            context.Session.Database.Settings.ShowForegroundPrivilegeMarker = true;
            context.AutoStartService.Setup(service => service.IsAutoStartEnabled()).ReturnsAsync(false);

            var persistence = new Mock<IMainConfigPersistence>(MockBehavior.Strict);
            var markerService = new Mock<IForegroundPrivilegeMarkerService>(MockBehavior.Strict);
            var sequence = new MockSequence();
            persistence.InSequence(sequence)
                .Setup(service => service.SaveConfig(
                    It.IsAny<AppDatabase>(),
                    It.IsAny<ISecureSecretSnapshotSource>(),
                    It.IsAny<byte[]>()));
            markerService.InSequence(sequence)
                .Setup(service => service.SetMarkerWindowEnabled(false));

            using var overrideScope = context.Scope.BeginLifetimeScope(builder =>
            {
                builder.RegisterInstance(persistence.Object).As<IMainConfigPersistence>();
                builder.RegisterInstance(markerService.Object).As<IForegroundPrivilegeMarkerService>();
                builder.RegisterType<OptionsSettingsHandler>().AsSelf().SingleInstance();
                builder.RegisterType<OptionsForegroundPrivilegeMarkerSection>().AsSelf().SingleInstance();
                builder.RegisterType<OptionsPanel>().AsSelf().InstancePerDependency();
            });

            using var panel = overrideScope.Resolve<OptionsPanel>();
            StaTestHelper.CreateControlTree(panel);
            panel.SetData(context.Session);
            StaTestHelper.PumpUntil(() => FindForegroundPrivilegeMarkerCheckBox(panel).Checked);

            FindForegroundPrivilegeMarkerCheckBox(panel).Checked = false;
            Application.DoEvents();

            Assert.False(context.Session.Database.Settings.ShowForegroundPrivilegeMarker);
            markerService.Verify(service => service.SetMarkerWindowEnabled(false), Times.Once);
            persistence.VerifyAll();
        });
    }

    [Fact]
    public void ForegroundPrivilegeMarkerToggle_Disabling_ShowsWarningAfterSaveWhenMarkerUnavailable()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreatePanelContext();
            context.Session.Database.Settings.ShowForegroundPrivilegeMarker = true;
            context.AutoStartService.Setup(service => service.IsAutoStartEnabled()).ReturnsAsync(false);

            var persistence = new Mock<IMainConfigPersistence>(MockBehavior.Strict);
            var markerService = new Mock<IForegroundPrivilegeMarkerService>(MockBehavior.Strict);
            var messageBox = new Mock<IMessageBoxService>(MockBehavior.Strict);
            var sequence = new MockSequence();
            persistence.InSequence(sequence)
                .Setup(service => service.SaveConfig(
                    It.IsAny<AppDatabase>(),
                    It.IsAny<ISecureSecretSnapshotSource>(),
                    It.IsAny<byte[]>()));
            markerService.InSequence(sequence)
                .Setup(service => service.SetMarkerWindowEnabled(false))
                .Throws(new InvalidOperationException("marker runtime unavailable"));
            messageBox.InSequence(sequence)
                .Setup(service => service.Show(
                    ForegroundMarkerWarningText,
                    "Foreground Marker Unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning))
                .Returns(DialogResult.OK);

            using var overrideScope = context.Scope.BeginLifetimeScope(builder =>
            {
                builder.RegisterInstance(persistence.Object).As<IMainConfigPersistence>();
                builder.RegisterInstance(markerService.Object).As<IForegroundPrivilegeMarkerService>();
                builder.RegisterInstance(messageBox.Object).As<IMessageBoxService>();
                builder.RegisterType<OptionsSettingsHandler>().AsSelf().SingleInstance();
                builder.RegisterType<OptionsForegroundPrivilegeMarkerSection>().AsSelf().SingleInstance();
                builder.RegisterType<OptionsPanel>().AsSelf().InstancePerDependency();
            });

            using var panel = overrideScope.Resolve<OptionsPanel>();
            StaTestHelper.CreateControlTree(panel);
            panel.SetData(context.Session);
            StaTestHelper.PumpUntil(() => FindForegroundPrivilegeMarkerCheckBox(panel).Checked);

            FindForegroundPrivilegeMarkerCheckBox(panel).Checked = false;
            Application.DoEvents();

            Assert.False(context.Session.Database.Settings.ShowForegroundPrivilegeMarker);
            persistence.VerifyAll();
            markerService.Verify(service => service.SetMarkerWindowEnabled(false), Times.Once);
            messageBox.Verify(
                service => service.Show(
                    ForegroundMarkerWarningText,
                    "Foreground Marker Unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning),
                Times.Once);
        });
    }

    [Fact]
    public void ForegroundPrivilegeMarkerToggle_Enabling_ShowsWarningAfterSaveWhenMarkerUnavailable()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreatePanelContext();
            context.Session.Database.Settings.ShowForegroundPrivilegeMarker = false;
            context.AutoStartService.Setup(service => service.IsAutoStartEnabled()).ReturnsAsync(false);

            var persistence = new Mock<IMainConfigPersistence>(MockBehavior.Strict);
            var markerService = new Mock<IForegroundPrivilegeMarkerService>(MockBehavior.Strict);
            var messageBox = new Mock<IMessageBoxService>(MockBehavior.Strict);
            var sequence = new MockSequence();
            persistence.InSequence(sequence)
                .Setup(service => service.SaveConfig(
                    It.IsAny<AppDatabase>(),
                    It.IsAny<ISecureSecretSnapshotSource>(),
                    It.IsAny<byte[]>()));
            markerService.InSequence(sequence)
                .Setup(service => service.SetMarkerWindowEnabled(true))
                .Throws(new InvalidOperationException("marker runtime unavailable"));
            messageBox.InSequence(sequence)
                .Setup(service => service.Show(
                    ForegroundMarkerWarningText,
                    "Foreground Marker Unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning))
                .Returns(DialogResult.OK);

            using var overrideScope = context.Scope.BeginLifetimeScope(builder =>
            {
                builder.RegisterInstance(persistence.Object).As<IMainConfigPersistence>();
                builder.RegisterInstance(markerService.Object).As<IForegroundPrivilegeMarkerService>();
                builder.RegisterInstance(messageBox.Object).As<IMessageBoxService>();
                builder.RegisterType<OptionsSettingsHandler>().AsSelf().SingleInstance();
                builder.RegisterType<OptionsForegroundPrivilegeMarkerSection>().AsSelf().SingleInstance();
                builder.RegisterType<OptionsPanel>().AsSelf().InstancePerDependency();
            });

            using var panel = overrideScope.Resolve<OptionsPanel>();
            StaTestHelper.CreateControlTree(panel);
            panel.SetData(context.Session);
            StaTestHelper.PumpUntil(() => !FindForegroundPrivilegeMarkerCheckBox(panel).Checked);

            FindForegroundPrivilegeMarkerCheckBox(panel).Checked = true;
            Application.DoEvents();

            Assert.True(context.Session.Database.Settings.ShowForegroundPrivilegeMarker);
            persistence.VerifyAll();
            markerService.Verify(service => service.SetMarkerWindowEnabled(true), Times.Once);
            messageBox.Verify(
                service => service.Show(
                    ForegroundMarkerWarningText,
                    "Foreground Marker Unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning),
                Times.Once);
        });
    }

    [Fact]
    public void ForegroundPrivilegeMarkerSection_Toggle_WhenRuntimeDisposed_SavesPreferenceAndShowsWarning()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreatePanelContext();
            var messageBox = new Mock<IMessageBoxService>(MockBehavior.Strict);
            var markerService = new Mock<IForegroundPrivilegeMarkerService>(MockBehavior.Strict);
            var saveCount = 0;
            markerService.Setup(service => service.SetMarkerWindowEnabled(false))
                .Throws(new ObjectDisposedException("marker runtime"));
            messageBox
                .Setup(service => service.Show(
                    ForegroundMarkerWarningText,
                    "Foreground Marker Unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning))
                .Returns(DialogResult.OK);
            context.Session.Database.Settings.ShowForegroundPrivilegeMarker = true;

            var section = new OptionsForegroundPrivilegeMarkerSection(markerService.Object, messageBox.Object);
            using var checkBox = new CheckBox();
            using var fullscreenCheckBox = new CheckBox();
            section.Initialize(
                checkBox,
                fullscreenCheckBox,
                () => context.Session.Database.Settings,
                () => saveCount++);
            section.ApplyLoadedState(true, true);

            checkBox.Checked = false;
            Application.DoEvents();

            Assert.False(context.Session.Database.Settings.ShowForegroundPrivilegeMarker);
            Assert.Equal(1, saveCount);
            markerService.Verify(service => service.SetMarkerWindowEnabled(false), Times.Once);
            messageBox.Verify(
                service => service.Show(
                    ForegroundMarkerWarningText,
                    "Foreground Marker Unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning),
                Times.Once);
        });
    }

    [Fact]
    public void ForegroundPrivilegeMarkerFullscreenToggle_SavesPreferenceAndUpdatesRuntime()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreatePanelContext();
            context.Session.Database.Settings.ShowForegroundPrivilegeMarker = true;
            context.Session.Database.Settings.ShowForegroundPrivilegeMarkerWhenFullscreen = true;
            context.AutoStartService.Setup(service => service.IsAutoStartEnabled()).ReturnsAsync(false);

            var persistence = new Mock<IMainConfigPersistence>(MockBehavior.Strict);
            var markerService = new Mock<IForegroundPrivilegeMarkerService>(MockBehavior.Strict);
            var sequence = new MockSequence();
            persistence.InSequence(sequence)
                .Setup(service => service.SaveConfig(
                    It.IsAny<AppDatabase>(),
                    It.IsAny<ISecureSecretSnapshotSource>(),
                    It.IsAny<byte[]>()));
            markerService.InSequence(sequence)
                .Setup(service => service.SetMarkerWindowEnabledWhenFullscreen(false));

            using var overrideScope = context.Scope.BeginLifetimeScope(builder =>
            {
                builder.RegisterInstance(persistence.Object).As<IMainConfigPersistence>();
                builder.RegisterInstance(markerService.Object).As<IForegroundPrivilegeMarkerService>();
                builder.RegisterType<OptionsSettingsHandler>().AsSelf().SingleInstance();
                builder.RegisterType<OptionsForegroundPrivilegeMarkerSection>().AsSelf().SingleInstance();
                builder.RegisterType<OptionsPanel>().AsSelf().InstancePerDependency();
            });

            using var panel = overrideScope.Resolve<OptionsPanel>();
            StaTestHelper.CreateControlTree(panel);
            panel.SetData(context.Session);
            StaTestHelper.PumpUntil(() => FindFullscreenForegroundPrivilegeMarkerCheckBox(panel).Checked);

            FindFullscreenForegroundPrivilegeMarkerCheckBox(panel).Checked = false;
            Application.DoEvents();

            Assert.False(context.Session.Database.Settings.ShowForegroundPrivilegeMarkerWhenFullscreen);
            persistence.VerifyAll();
            markerService.Verify(service => service.SetMarkerWindowEnabledWhenFullscreen(false), Times.Once);
        });
    }

    private static OptionsUiTestContext CreatePanelContext() => OptionsUiTestContext.Create(7);

    private static CheckBox FindCheckBox(Control root, string text)
        => FindControls<CheckBox>(root).First(control => control.Text == text);

    private static CheckBox FindForegroundPrivilegeMarkerCheckBox(Control root)
        => FindControls<CheckBox>(root).Single(control => control.Name == "ForegroundPrivilegeMarkerCheckBox");

    private static CheckBox FindFullscreenForegroundPrivilegeMarkerCheckBox(Control root)
        => FindControls<CheckBox>(root).Single(control => control.Name == "ForegroundPrivilegeMarkerFullscreenCheckBox");

    private static T FindControl<T>(Control root, Func<T, bool> predicate) where T : Control
        => FindControls<T>(root).First(predicate);

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindControls<T>(child))
                yield return nested;
        }
    }

    private static void AssertHelp(ContextHelpForm host, Control control, string expected)
    {
        Assert.True(host.TryGetContextHelp(control, out var actual));
        Assert.Equal(expected, actual);
    }

    private static void NoDefaultContextHelpForDescendants(Control ancestor, ContextHelpForm host, string expected)
    {
        foreach (var helpControl in host.GetExplicitContextHelpControls())
        {
            if (ReferenceEquals(helpControl, ancestor))
                continue;
            if (!IsDescendantOf(helpControl, ancestor))
                continue;
            Assert.False(host.TryGetContextHelp(helpControl, out var actual) && actual == expected);
        }
    }

    private static bool IsDescendantOf(Control control, Control ancestor)
    {
        for (var current = control.Parent; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }
}
