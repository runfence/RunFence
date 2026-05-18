using RunFence.Account.UI.AppContainer;
using RunFence.Core.Models;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AppContainerCapabilitiesBinderTests
{
    [Fact]
    public void InitializeCapabilityRows_AddsKnownCapabilitiesAndLoopback()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var binder = new AppContainerCapabilitiesBinder(new FakeNotifier());
            using var flow = new FlowLayoutPanel();
            using var loopbackCheckBox = new CheckBox();

            var result = binder.InitializeCapabilityRows(flow, loopbackCheckBox);

            Assert.Equal(10, result.Length);
            Assert.Equal(11, flow.Controls.Count);
            Assert.Same(loopbackCheckBox, flow.Controls[^1]);
            Assert.Equal("internetClient", result[0].Text);
            Assert.Equal("S-1-15-3-1", result[0].Tag);
        });
    }

    [Fact]
    public void WireComToolbar_TracksRemoveEnabledStateFromSelection()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var binder = new AppContainerCapabilitiesBinder(new FakeNotifier());
            using var toolStrip = new ToolStrip();
            using var listBox = new ListBox();

            binder.WireComToolbar(toolStrip, listBox, components: null, owner: new Form(), _ => { });
            var removeButton = Assert.IsType<ToolStripButton>(toolStrip.Items[3]);
            Assert.False(removeButton.Enabled);

            listBox.Items.Add("{CLSID-1}");
            listBox.SelectedIndex = 0;

            Assert.True(removeButton.Enabled);
        });
    }

    [Fact]
    public void PopulateFromExisting_AppliesExistingCapabilitiesComAndSid()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var binder = new AppContainerCapabilitiesBinder(new FakeNotifier());
            var existing = new AppContainerEntry
            {
                Name = "rfn_browser",
                DisplayName = "Browser",
                Sid = "S-1-15-2-42",
                Capabilities = ["S-1-15-3-1", "S-1-15-3-4"],
                EnableLoopback = true,
                IsEphemeral = true,
                ComAccessClsids = ["{CLSID-1}", "{CLSID-2}"]
            };

            using var displayNameBox = new TextBox();
            using var profileNameBox = new TextBox();
            using var sidBox = new TextBox();
            using var loopbackCheckBox = new CheckBox();
            using var ephemeralCheckBox = new CheckBox();
            using var comListBox = new ListBox();
            using var flow = new FlowLayoutPanel();

            var capabilityCheckBoxes = binder.InitializeCapabilityRows(flow, loopbackCheckBox);
            binder.PopulateFromExisting(
                existing,
                displayNameBox,
                profileNameBox,
                sidBox,
                capabilityCheckBoxes,
                loopbackCheckBox,
                ephemeralCheckBox,
                comListBox);

            Assert.Equal("Browser", displayNameBox.Text);
            Assert.Equal("rfn_browser", profileNameBox.Text);
            Assert.Equal("S-1-15-2-42", sidBox.Text);
            Assert.True(loopbackCheckBox.Checked);
            Assert.True(ephemeralCheckBox.Checked);
            Assert.True(capabilityCheckBoxes[0].Checked);
            Assert.True(capabilityCheckBoxes[3].Checked);
            Assert.Equal(["{CLSID-1}", "{CLSID-2}"], comListBox.Items.Cast<string>().ToArray());
        });
    }

    [Fact]
    public void RefreshProfileNamePreview_TogglesPreviewAndReadOnlyForEphemeral()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var binder = new AppContainerCapabilitiesBinder(new FakeNotifier());
            using var displayNameBox = new TextBox { Text = "My Browser" };
            using var profileNameBox = new TextBox();
            using var ephemeralCheckBox = new CheckBox();

            binder.RefreshProfileNamePreview(existing: null, displayNameBox, profileNameBox, ephemeralCheckBox);
            Assert.Equal("rfn_my_browser", profileNameBox.Text);
            Assert.False(profileNameBox.ReadOnly);

            ephemeralCheckBox.Checked = true;
            binder.RefreshProfileNamePreview(existing: null, displayNameBox, profileNameBox, ephemeralCheckBox);
            Assert.Equal("(auto-generated)", profileNameBox.Text);
            Assert.True(profileNameBox.ReadOnly);
            Assert.Equal(SystemColors.Control, profileNameBox.BackColor);
        });
    }

    private sealed class FakeNotifier : IAppContainerEditDialogNotifier
    {
        public void ShowValidationWarning(IWin32Window owner, string message) { }

        public void ShowOperationError(IWin32Window owner, string message) { }

        public void ShowRestartRequired(IWin32Window owner) { }

        public void ShowComAccessWarning(IWin32Window owner, IReadOnlyList<string> warnings) { }

        public void ShowPersistenceWarning(IWin32Window owner, string message) { }
    }
}
