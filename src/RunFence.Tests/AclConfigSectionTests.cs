using Moq;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using RunFence.UI;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace RunFence.Tests;

public class AclConfigSectionTests
{
    [Fact]
    public void SetExePath_FileTargetConflict_UpdatesConflictAndTargetLabelsFromValidationState()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = CreateSection();
            var exePath = @"C:\Apps\Tool.exe";
            section.SetContext(new AclConfigContext(
                new TestAclConfigContextProvider(exePath),
                [
                    new AppEntry
                    {
                        Id = "other",
                        Name = "Other App",
                        ExePath = exePath,
                        RestrictAcl = true,
                        AclMode = AclMode.Allow,
                        AclTarget = AclTarget.File
                    }
                ],
                CurrentAppId: null));
            StaTestHelper.CreateControlTree(section);

            section.SetExePath(exePath, isFolder: false);
            FindRadioButton(section, "File only").Checked = true;

            Assert.Equal(
                "Another app (Other App) already manages ACLs on this path.",
                FindConflictLabel(section).Text);
            Assert.True(FindConflictLabel(section).Visible);
            Assert.Equal($"Target: {Path.GetFullPath(exePath)}", FindTargetLabel(section).Text);
        });
    }

    [Fact]
    public void SetExePath_FolderTargetAndDepth_UpdatesTargetLabelFromValidationState()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = CreateSection();
            string tempRoot;

            using (var temp = new TempDirectory())
            {
                tempRoot = temp.Path;

                var directoryPath = Path.Combine(temp.Path, "Child");
                Directory.CreateDirectory(directoryPath);
                var filePath = Path.Combine(directoryPath, "Tool.exe");
                File.WriteAllText(filePath, string.Empty);

                section.SetContext(new AclConfigContext(new TestAclConfigContextProvider(filePath), [], null));
                StaTestHelper.CreateControlTree(section);

                section.SetExePath(filePath, isFolder: false);
                Assert.Equal($"Target: {Path.GetDirectoryName(Path.GetFullPath(filePath))}", FindTargetLabel(section).Text);

                section.SelectFolderDepth(1);
                var expectedParent = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(filePath))!)!;
                Assert.Equal($"Target: {expectedParent}", FindTargetLabel(section).Text);
            }

            Assert.False(Directory.Exists(tempRoot));
        });
    }

    [Fact]
    public void SetExePath_UrlPath_UsesValidationStateTargetText()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = CreateSection();
            const string url = "steam://run/123";
            section.SetContext(new AclConfigContext(new TestAclConfigContextProvider(url), [], null));
            StaTestHelper.CreateControlTree(section);

            section.SetExePath(url, isFolder: false);

            Assert.Equal($"Target: {url}", FindTargetLabel(section).Text);
            Assert.False(FindConflictLabel(section).Visible);
        });
    }

    [Fact]
    public void SetExePath_BlockedPath_ShowsBlockedMessageAndResolvedTarget()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var exePath = @"C:\Blocked\Tool.exe";
            using var section = CreateSection(Path.GetFullPath(exePath));
            section.SetContext(new AclConfigContext(new TestAclConfigContextProvider(exePath), [], null));
            StaTestHelper.CreateControlTree(section);

            section.SetExePath(exePath, isFolder: false);
            FindRadioButton(section, "File only").Checked = true;

            Assert.Equal($"Cannot restrict access on: {Path.GetFullPath(exePath)}", FindConflictLabel(section).Text);
            Assert.Equal($"Target: {Path.GetFullPath(exePath)}", FindTargetLabel(section).Text);
        });
    }

    [Fact]
    public void ContextPathChange_RefreshUsesCurrentProviderPathInsteadOfStaleSetExePathValue()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = CreateSection();
            var provider = new TestAclConfigContextProvider(@"C:\Apps\Old\Tool.exe");
            section.SetContext(new AclConfigContext(provider, [], null));
            StaTestHelper.CreateControlTree(section);

            section.SetExePath(provider.ExePath, isFolder: false);
            provider.ExePath = @"C:\Apps\New\Tool.exe";
            FindRadioButton(section, "File only").Checked = true;

            Assert.Equal($@"Target: {Path.GetFullPath(provider.ExePath)}", FindTargetLabel(section).Text);
        });
    }

    [Fact]
    public void AllowModeToggle_PrePopulationRefreshesValidationTargetHeightAndLayout()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = CreateSection();
            var exePath = @"C:\Apps\Tool.exe";
            section.SetContext(new AclConfigContext(new TestAclConfigContextProvider(exePath), [], null));
            StaTestHelper.CreateControlTree(section);
            section.SetExePath(exePath, isFolder: false);

            var layoutChangedCount = 0;
            section.LayoutChanged += () => layoutChangedCount++;

            FindRadioButton(section, "Allow mode — explicit allowlist (breaks inheritance)").Checked = true;

            Assert.Single(FindAllowEntriesGrid(section).Rows.Cast<DataGridViewRow>(), row => row.Tag is AllowAclEntry);
            Assert.Equal(string.Empty, FindConflictLabel(section).Text);
            Assert.False(FindConflictLabel(section).Visible);
            Assert.Equal($"Target: {Path.GetDirectoryName(Path.GetFullPath(exePath))}", FindTargetLabel(section).Text);
            Assert.Equal(FindAllowPanel(section).Bottom + 6, section.Height);
            Assert.Equal(1, layoutChangedCount);
        });
    }

    [Fact]
    public void AllowEntryRemoval_RefreshesValidationTargetHeightAndLayoutImmediately()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = CreateSection();
            var exePath = @"C:\Apps\Tool.exe";
            section.SetContext(new AclConfigContext(new TestAclConfigContextProvider(exePath), [], null));
            section.PopulateFromExisting(new AclConfigInitializationModel(
                RestrictAcl: true,
                AclMode: AclMode.Allow,
                DeniedRights: DeniedRights.Execute,
                AllowedAclEntries:
                [
                    new AllowAclEntry
                    {
                        Sid = "S-1-5-21-1",
                        AllowExecute = true,
                        AllowWrite = false
                    }
                ],
                AclTarget: AclTarget.Folder,
                FolderAclDepth: 0));
            StaTestHelper.CreateControlTree(section);
            section.SetExePath(exePath, isFolder: false);

            var layoutChangedCount = 0;
            section.LayoutChanged += () => layoutChangedCount++;

            var grid = FindAllowEntriesGrid(section);
            grid.Rows[0].Selected = true;
            FindToolStripButton(section, "Remove account").PerformClick();
            Application.DoEvents();

            Assert.DoesNotContain(grid.Rows.Cast<DataGridViewRow>(), row => row.Tag is AllowAclEntry);
            Assert.Equal("Allow mode requires at least one entry.", FindConflictLabel(section).Text);
            Assert.True(FindConflictLabel(section).Visible);
            Assert.Equal($"Target: {Path.GetDirectoryName(Path.GetFullPath(exePath))}", FindTargetLabel(section).Text);
            Assert.Equal(FindConflictLabel(section).Bottom + 6, section.Height);
            Assert.Equal(1, layoutChangedCount);
        });
    }

    [Fact]
    public void AllowModeToggle_WithExistingEntries_DoesNotDuplicatePrePopulatedEntries()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = CreateSection();
            var exePath = @"C:\Apps\Tool.exe";
            section.SetContext(new AclConfigContext(new TestAclConfigContextProvider(exePath), [], null));
            section.PopulateFromExisting(new AclConfigInitializationModel(
                RestrictAcl: true,
                AclMode: AclMode.Allow,
                DeniedRights: DeniedRights.Execute,
                AllowedAclEntries:
                [
                    new AllowAclEntry
                    {
                        Sid = "S-1-5-21-1",
                        AllowExecute = true,
                        AllowWrite = false
                    }
                ],
                AclTarget: AclTarget.Folder,
                FolderAclDepth: 0));
            StaTestHelper.CreateControlTree(section);
            section.SetExePath(exePath, isFolder: false);

            FindRadioButton(section, "Deny mode — deny other accounts").Checked = true;
            FindRadioButton(section, "Allow mode — explicit allowlist (breaks inheritance)").Checked = true;

            Assert.Single(FindAllowEntriesGrid(section).Rows.Cast<DataGridViewRow>(), row => row.Tag is AllowAclEntry);
        });
    }

    private static AclConfigSection CreateSection(params string[] blockedPaths)
    {
        var blockedSet = new HashSet<string>(
            blockedPaths.Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
        var aclService = new Mock<IAclService>();
        aclService.Setup(service => service.ResolveAclTargetPath(It.IsAny<AppEntry>()))
            .Returns<AppEntry>(ResolveTargetPath);
        aclService.Setup(service => service.IsBlockedPath(It.IsAny<string>()))
            .Returns<string>(path => blockedSet.Contains(Path.GetFullPath(path)));

        var sidResolver = new Mock<ISidResolver>();
        sidResolver.Setup(service => service.TryResolveName(It.IsAny<string>())).Returns<string>(sid => sid);
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var displayNameResolver = new SidDisplayNameResolver(sidResolver.Object, profilePathResolver.Object);

        return new AclConfigSection(
            new AclAllowListGridHandler(),
            new AllowListEntryFactory(
                Mock.Of<ILocalUserProvider>(),
                Mock.Of<ILocalGroupQueryService>(),
                Mock.Of<ISidEntryHelper>(),
                displayNameResolver),
            new AclConfigValidator(aclService.Object, Mock.Of<ILoggingService>()),
            new FolderDepthHelper(aclService.Object, Mock.Of<ILoggingService>()));
    }

    private static string ResolveTargetPath(AppEntry app)
    {
        if (app.AclTarget == AclTarget.File)
            return Path.GetFullPath(app.ExePath);

        var folder = app.IsFolder
            ? Path.GetFullPath(app.ExePath)
            : Path.GetDirectoryName(Path.GetFullPath(app.ExePath))!;

        for (var i = 0; i < app.FolderAclDepth; i++)
        {
            var parent = Path.GetDirectoryName(folder);
            if (parent == null)
                break;

            folder = parent;
        }

        return folder;
    }

    private static Label FindTargetLabel(Control root)
        => EnumerateControls(root).OfType<Label>().Single(label => label.ForeColor == Color.DarkBlue);

    private static Label FindConflictLabel(Control root)
        => EnumerateControls(root).OfType<Label>().Single(label => label.ForeColor == Color.Red);

    private static RadioButton FindRadioButton(Control root, string text)
        => EnumerateControls(root).OfType<RadioButton>().Single(radioButton => radioButton.Text == text);

    private static ToolStripButton FindToolStripButton(Control root, string toolTipText)
        => EnumerateControls(root).OfType<ToolStrip>()
            .SelectMany(toolStrip => toolStrip.Items.OfType<ToolStripButton>())
            .Single(button => button.ToolTipText == toolTipText);

    private static DataGridView FindAllowEntriesGrid(Control root)
        => EnumerateControls(root).OfType<DataGridView>().Single();

    private static Panel FindAllowPanel(Control root)
        => EnumerateControls(root).OfType<Panel>()
            .Single(panel => panel.Controls.OfType<DataGridView>().Any());

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;

        foreach (Control child in root.Controls)
        {
            foreach (var nested in EnumerateControls(child))
                yield return nested;
        }
    }

    private sealed class TestAclConfigContextProvider(string exePath) : IAclConfigContextProvider
    {
        public string ExePath { get; set; } = exePath;

        public string GetExePath() => ExePath;

        public string? GetSelectedAccountSid() => "S-1-5-21-1";

        public bool IsContainerSelected() => false;

        public void OnSidNameLearned(string sid, string name)
        {
        }
    }
}
