using Moq;
using RunFence.Account;
using RunFence.Acl.Permissions;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Infrastructure;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Persistence.UI.Forms;
using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class ConfigManagerSectionTests
{
    [Fact]
    public void RegisterContextHelp_RegistersWorkflowTargets()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var pinKey = TestSecretFactory.Create(32);
            var sessionProvider = new Mock<ISessionProvider>();
            sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithOwnedPinDerivedKey(pinKey));

            using var host = new ContextHelpForm();
            var section = new ConfigManagerSection(
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

            section.RegisterContextHelp(host);

            var group = FindControls<GroupBox>(section).Single();
            var toolStrip = FindControls<ToolStrip>(section).Single();
            var listBox = FindControls<ListBox>(section).Single();

            Assert.False(host.TryGetContextHelp(group, out _));
            Assert.False(host.TryGetContextHelp(toolStrip, out _));
            Assert.False(host.TryGetContextHelp(listBox, out _));
            Assert.Empty(host.GetExplicitContextHelpToolStripDropDowns());
        });
    }

    [Fact]
    public void OnNewConfigClick_AclRestrictionFailsAndUserDeclines_DoesNotLoadUnrestrictedConfig()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var pinKey = TestSecretFactory.Create(32);
            using var tempDir = new TempDirectory("RunFence_ConfigManagerSection");
            var configPath = Path.Combine(tempDir.Path, "new-config.rfn");

            var appConfigService = new Mock<IAppConfigService>();
            var appFilter = new Mock<IAppFilter>();
            var log = new Mock<ILoggingService>();
            var aclPermission = new Mock<IAclPermissionService>();
            var messageBoxService = new Mock<IMessageBoxService>();
            var sessionProvider = new Mock<ISessionProvider>();
            sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithOwnedPinDerivedKey(pinKey));

            aclPermission.Setup(s => s.RestrictToAdmins(configPath))
                .Throws(new UnauthorizedAccessException("Access denied"));
            messageBoxService.SetupSequence(s => s.Show(
                    It.IsAny<string>(),
                    "Set Permissions",
                    MessageBoxButtons.YesNo,
                    It.IsAny<MessageBoxIcon>()))
                .Returns(DialogResult.Yes)
                .Returns(DialogResult.No);

            var section = new ConfigManagerSection(
                appConfigService.Object,
                appFilter.Object,
                log.Object,
                aclPermission.Object,
                null!,
                null!,
                sessionProvider.Object,
                Mock.Of<IAccountSidResolutionService>(),
                null!,
                messageBoxService.Object)
            {
                SaveFilePathSelector = (_, _) => configPath
            };

            var loadRequested = false;
            section.ConfigLoadRequested += _ => loadRequested = true;

            FindToolbarButton(section, "New config...").PerformClick();

            Assert.False(loadRequested);
            appConfigService.Verify(
                s => s.CreateEmptyConfig(configPath, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()),
                Times.Once);
        });
    }

    [Fact]
    public void OnNewConfigClick_AclRestrictionFailsAndUserConfirms_LoadsUnrestrictedConfig()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var pinKey = TestSecretFactory.Create(32);
            using var tempDir = new TempDirectory("RunFence_ConfigManagerSection");
            var configPath = Path.Combine(tempDir.Path, "new-config.rfn");

            var appConfigService = new Mock<IAppConfigService>();
            var appFilter = new Mock<IAppFilter>();
            var log = new Mock<ILoggingService>();
            var aclPermission = new Mock<IAclPermissionService>();
            var messageBoxService = new Mock<IMessageBoxService>();
            var sessionProvider = new Mock<ISessionProvider>();
            sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithOwnedPinDerivedKey(pinKey));

            aclPermission.Setup(s => s.RestrictToAdmins(configPath))
                .Throws(new UnauthorizedAccessException("Access denied"));
            messageBoxService.SetupSequence(s => s.Show(
                    It.IsAny<string>(),
                    "Set Permissions",
                    MessageBoxButtons.YesNo,
                    It.IsAny<MessageBoxIcon>()))
                .Returns(DialogResult.Yes)
                .Returns(DialogResult.Yes);

            var section = new ConfigManagerSection(
                appConfigService.Object,
                appFilter.Object,
                log.Object,
                aclPermission.Object,
                null!,
                null!,
                sessionProvider.Object,
                Mock.Of<IAccountSidResolutionService>(),
                null!,
                messageBoxService.Object)
            {
                SaveFilePathSelector = (_, _) => configPath
            };

            string? loadedPath = null;
            section.ConfigLoadRequested += path => loadedPath = path;

            FindToolbarButton(section, "New config...").PerformClick();

            Assert.Equal(configPath, loadedPath);
        });
    }

    private static ToolStripButton FindToolbarButton(Control root, string toolTipText)
        => FindControls<ToolStrip>(root)
            .SelectMany(toolStrip => toolStrip.Items.OfType<ToolStripButton>())
            .Single(button => string.Equals(button.ToolTipText, toolTipText, StringComparison.Ordinal));

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
}
