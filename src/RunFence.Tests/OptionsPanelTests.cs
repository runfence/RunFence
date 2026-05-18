using Autofac;
using Moq;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge.UI.Forms;
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
    [Fact]
    public void SetData_LoadsCurrentSettingsIntoControls()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreatePanelContext();
            context.Session.Database.Settings.IdleTimeoutMinutes = 45;
            context.Session.Database.Settings.AutoLockInBackground = true;
            context.Session.Database.Settings.AutoLockTimeoutMinutes = 9;
            context.Session.Database.Settings.FolderBrowserExePath = @"C:\Tools\browser.exe";
            context.Session.Database.Settings.FolderBrowserArguments = "--open \"%1\"";
            context.Session.Database.Settings.DefaultDesktopSettingsPath = @"C:\Profiles\desktop.rfn";
            context.Session.Database.Settings.UnlockMode = UnlockMode.AdminAndPin;
            context.Session.Database.Settings.EnableRunAsContextMenu = true;
            context.Session.Database.Settings.LogVerbosity = LogVerbosity.Debug;
            context.AutoStartService.Setup(service => service.IsAutoStartEnabled()).ReturnsAsync(true);
            context.SetRememberPinEnabled(false);

            using var panel = context.Scope.Resolve<OptionsPanel>();
            StaTestHelper.CreateControlTree(panel);
            panel.SetData(context.Session);

            StaTestHelper.PumpUntil(() => FindComboBoxes(panel).Any(comboBox => comboBox.Items.Count == 5));
            StaTestHelper.PumpUntil(() => FindCheckBox(panel, "Auto-start on login").Checked);

            var logVerbosityComboBox = FindComboBoxes(panel).Single(comboBox => comboBox.Items.Count == 5);
            var unlockModeComboBox = FindComboBoxes(panel).Single(comboBox => comboBox.Items.Count == 4);

            Assert.Equal(5, logVerbosityComboBox.Items.Count);
            Assert.True(FindCheckBox(panel, "Auto-start on login").Checked);
            Assert.True(FindCheckBox(panel, "Exit after app idle").Checked);
            Assert.True(FindCheckBox(panel, "Lock in background").Checked);
            Assert.True(FindCheckBox(panel, "Enable 'RunFence...' context menu for files").Checked);
            Assert.Equal(45, FindNumericUpDowns(panel)[0].Value);
            Assert.Equal(9, FindNumericUpDowns(panel)[1].Value);
            var textBoxValues = FindTextBoxes(panel).Select(textBox => textBox.Text).ToList();
            Assert.Contains(@"C:\Tools\browser.exe", textBoxValues);
            Assert.Contains("--open \"%1\"", textBoxValues);
            Assert.Contains(@"C:\Profiles\desktop.rfn", textBoxValues);
            Assert.Equal(2, unlockModeComboBox.SelectedIndex);
            Assert.Equal("Debug", logVerbosityComboBox.SelectedItem?.ToString());
        });
    }

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

            var folderBrowserGroup = FindControl<GroupBox>(panel, control => control.Text == "Folder Browser");
            var desktopSettingsGroup = FindControl<GroupBox>(panel, control => control.Text == "Desktop Settings");
            var dragBridgeGroup = FindControl<GroupBox>(panel, control => control.Text == "Drag Bridge (Cross-Account Drag & Drop)");

            AssertHelp(host, FindCheckBox(panel, "Block ICMP when Internet is blocked"), ContextHelpTextCatalog.Options_FirewallIcmp);
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
            Assert.Empty(host.GetExplicitContextHelpToolStripDropDowns());
            Assert.Empty(host.GetExplicitContextHelpToolStripItems());
        });
    }

    [Fact]
    public void OpenLogButton_UsesOptionsMaintenanceLaunchHandler()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var context = CreatePanelContext();
            var log = new Mock<ILoggingService>();
            log.SetupGet(x => x.LogFilePath).Returns(@"C:\Logs\runfence.log");
            var launchFacade = new Mock<ILaunchFacade>();
            launchFacade
                .Setup(x => x.LaunchFile(
                    It.IsAny<string>(),
                    It.IsAny<LaunchIdentity>(),
                    It.IsAny<string?>(),
                    It.IsAny<Func<string, string, bool>?>()))
                .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));

            using var overrideScope = context.Scope.BeginLifetimeScope(builder =>
            {
                builder.RegisterInstance(log.Object).As<ILoggingService>();
                builder.RegisterInstance(launchFacade.Object).As<ILaunchFacade>();
                builder.RegisterInstance(new Mock<ILaunchFeedbackPresenter>().Object).As<ILaunchFeedbackPresenter>();
                builder.RegisterInstance(new Mock<IMessageBoxService>().Object).As<IMessageBoxService>();
                builder.RegisterType<OptionsMaintenanceLaunchHandler>().AsSelf().SingleInstance();
                builder.RegisterType<OptionsPanel>().AsSelf().InstancePerDependency();
            });

            using var panel = overrideScope.Resolve<OptionsPanel>();
            StaTestHelper.CreateControlTree(panel);

            FindButton(panel, "Open Log").PerformClick();

            launchFacade.Verify(
                x => x.LaunchFile(
                    @"C:\Logs\runfence.log",
                    It.IsAny<LaunchIdentity>(),
                    null,
                    It.IsAny<Func<string, string, bool>?>()),
                Times.Once);
        });
    }

    private static OptionsUiTestContext CreatePanelContext() => OptionsUiTestContext.Create(7);

    private static Button FindButton(Control root, string text)
        => FindControls<Button>(root).First(control => control.Text == text);

    private static CheckBox FindCheckBox(Control root, string text)
        => FindControls<CheckBox>(root).First(control => control.Text == text);

    private static T FindControl<T>(Control root, Func<T, bool> predicate) where T : Control
        => FindControls<T>(root).First(predicate);

    private static List<ComboBox> FindComboBoxes(Control root)
        => FindControls<ComboBox>(root).ToList();

    private static List<NumericUpDown> FindNumericUpDowns(Control root)
        => FindControls<NumericUpDown>(root).ToList();

    private static List<TextBox> FindTextBoxes(Control root)
        => FindControls<TextBox>(root).ToList();

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
