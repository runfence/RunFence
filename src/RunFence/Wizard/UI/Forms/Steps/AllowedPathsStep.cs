using System.ComponentModel;
using RunFence.UI;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for building a list of folder paths the new account will be allowed to access.
/// Used by the Browser template for allowed download/document folders.
/// </summary>
public class AllowedPathsStep : WizardStepPage
{
    private readonly Action<List<string>> _setPaths;
    private readonly string _labelText;
    private readonly string _stepTitle;

    private Label _infoLabel = null!;
    private ToolStrip _toolStrip = null!;
    private ToolStripButton _addButton = null!;
    private ToolStripButton _removeButton = null!;
    private ListBox _pathListBox = null!;
    private ContextMenuStrip _contextMenu = null!;
    private ToolStripMenuItem _ctxAdd = null!;
    private ToolStripMenuItem _ctxRemove = null!;

    public AllowedPathsStep(
        Action<List<string>> setPaths,
        string? labelText = null,
        string? stepTitle = null)
    {
        _setPaths = setPaths;
        _labelText = labelText ?? "Add folders this account should be able to access:";
        _stepTitle = stepTitle ?? "Allowed Folders";
        BuildContent();
    }

    public override string StepTitle => _stepTitle;

    public override string? Validate() => null;

    public override void Collect()
    {
        _setPaths(_pathListBox.Items.Cast<string>().ToList());
    }

    private void BuildContent()
    {
        SuspendLayout();
        Padding = new Padding(8);

        _infoLabel = new Label
        {
            Text = _labelText,
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 8)
        };
        TrackWrappingLabel(_infoLabel);

        _toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            BackColor = Color.White
        };
        _addButton = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Add Folder…",
            Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22))
        };
        _addButton.Click += OnAddClick;

        _removeButton = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Remove",
            Enabled = false,
            Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33))
        };
        _removeButton.Click += OnRemoveClick;
        _toolStrip.Items.AddRange(_addButton, _removeButton);

        _pathListBox = new ListBox
        {
            Font = new Font("Segoe UI", 9),
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.One,
            IntegralHeight = false
        };
        _pathListBox.SelectedIndexChanged += (_, _) => UpdateButtons();
        _pathListBox.MouseDown += OnMouseDown;
        _pathListBox.KeyDown += OnKeyDown;

        _contextMenu = new ContextMenuStrip();
        _ctxAdd = new ToolStripMenuItem("Add Folder…")
        {
            Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 16)
        };
        _ctxAdd.Click += OnAddClick;
        _ctxRemove = new ToolStripMenuItem("Remove")
        {
            Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16)
        };
        _ctxRemove.Click += OnRemoveClick;
        _contextMenu.Items.AddRange(_ctxAdd, _ctxRemove);
        _contextMenu.Opening += OnContextMenuOpening;
        _pathListBox.ContextMenuStrip = _contextMenu;

        // Fill first, then Top items (last added = topmost in DockStyle.Top stack).
        Controls.Add(_pathListBox);
        Controls.Add(_toolStrip);
        Controls.Add(_infoLabel);
        ResumeLayout(false);
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog();
        dlg.Description = "Select a folder";
        dlg.UseDescriptionForTitle = true;
        dlg.ShowNewFolderButton = false;

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var path = dlg.SelectedPath;
            if (!_pathListBox.Items.Contains(path))
                _pathListBox.Items.Add(path);
        }
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_pathListBox.SelectedIndex >= 0)
            _pathListBox.Items.RemoveAt(_pathListBox.SelectedIndex);
    }

    private void UpdateButtons()
    {
        _removeButton.Enabled = _pathListBox.SelectedIndex >= 0;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            _pathListBox.SelectedIndex = _pathListBox.IndexFromPoint(e.Location);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && _pathListBox.SelectedIndex >= 0)
            OnRemoveClick(sender, e);
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        _ctxAdd.Visible = _pathListBox.SelectedIndex < 0;
        _ctxRemove.Visible = _pathListBox.SelectedIndex >= 0;
    }
}