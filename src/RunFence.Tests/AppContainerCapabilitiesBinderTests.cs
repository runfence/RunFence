using RunFence.Account.UI.AppContainer;
using RunFence.Core.Models;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AppContainerCapabilitiesBinderTests
{
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
