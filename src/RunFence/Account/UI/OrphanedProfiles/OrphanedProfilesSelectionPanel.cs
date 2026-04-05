using RunFence.Account.OrphanedProfiles;

namespace RunFence.Account.UI.OrphanedProfiles;

public partial class OrphanedProfilesSelectionPanel : UserControl
{
    public IReadOnlyList<OrphanedProfile> CheckedProfiles =>
        _profileList.CheckedItems.Cast<OrphanedProfile>().ToList();

    public void Populate(List<OrphanedProfile> profiles)
    {
        bool hasProfiles = profiles.Count > 0;

        _descLabel.Text = hasProfiles
            ? "The following orphaned profile directories were found:"
            : "No orphaned profiles found.";

        _profileList.Items.Clear();
        foreach (var p in profiles)
            _profileList.Items.Add(p, isChecked: true);

        _profileList.Visible = hasProfiles;
        _selectAllButton.Enabled = hasProfiles;
        _selectAllButton.Visible = hasProfiles;
        _deselectAllButton.Enabled = hasProfiles;
        _deselectAllButton.Visible = hasProfiles;
    }

    private void OnSelectAllClick(object? sender, EventArgs e)
    {
        for (int i = 0; i < _profileList.Items.Count; i++)
            _profileList.SetItemChecked(i, true);
    }

    private void OnDeselectAllClick(object? sender, EventArgs e)
    {
        for (int i = 0; i < _profileList.Items.Count; i++)
            _profileList.SetItemChecked(i, false);
    }
}