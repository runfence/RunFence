using System.ComponentModel;
using RunFence.UI;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Inline handler association list editor for the AppEditDialog.
/// Shows this app's associations (filtered from effective mappings).
/// </summary>
public partial class HandlerAssociationsSection : UserControl
{
    public event Action? Changed;

    public HandlerAssociationsSection()
    {
        InitializeComponent();
        _addButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22));
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33));
        _ctxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);
    }

    public void SetAssociations(List<string>? associations)
    {
        _listBox.Items.Clear();
        if (associations == null)
            return;
        foreach (var key in associations)
            _listBox.Items.Add(key);
    }

    public List<string>? GetAssociations()
    {
        if (_listBox.Items.Count == 0)
            return null;
        return _listBox.Items.Cast<string>().ToList();
    }

    public void SetEnabled(bool enabled)
    {
        _listBox.Enabled = enabled;
        _addButton.Enabled = enabled;
        _removeButton.Enabled = enabled && _listBox.SelectedIndex >= 0;
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        using var dlg = new AssociationKeyInputDialog(
            "Add Association",
            AppHandlerRegistrationService.CommonAssociationSuggestions);

        if (dlg.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        var key = dlg.SelectedKey;
        if (string.IsNullOrEmpty(key))
            return;

        if (!AppHandlerRegistrationService.IsValidKey(key))
        {
            MessageBox.Show("Invalid association key. Use a file extension (e.g., .pdf) or protocol name (e.g., http).",
                "Invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Check if already in this app's list
        if (_listBox.Items.Cast<string>().Any(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("This association is already in the list.",
                "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _listBox.Items.Add(key);
        Changed?.Invoke();
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_listBox.SelectedIndex < 0)
            return;
        _listBox.Items.RemoveAt(_listBox.SelectedIndex);
        Changed?.Invoke();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _removeButton.Enabled = _listBox.Enabled && _listBox.SelectedIndex >= 0;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && _listBox.SelectedIndex >= 0)
            OnRemoveClick(sender, e);
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var index = _listBox.IndexFromPoint(e.Location);
            _listBox.SelectedIndex = index;
        }
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        _ctxAdd.Visible = _listBox.SelectedIndex < 0;
        _ctxRemove.Visible = _listBox.SelectedIndex >= 0;
    }
}