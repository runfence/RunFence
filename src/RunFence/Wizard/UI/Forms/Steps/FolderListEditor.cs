using System.ComponentModel;
using RunFence.Infrastructure;
using RunFence.UI;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Reusable UI component for managing a list of paths or folders.
/// Encapsulates a ToolStrip with Add/Remove buttons, a ListBox, and a context menu.
/// Supports folder browse and executable file browse dialog types.
/// </summary>
/// <remarks>
/// Call <see cref="Initialize"/> after construction to set the label text and browse mode.
/// Use <see cref="AddExtraToolbarButton"/> / <see cref="AddExtraContextMenuItem"/> to insert
/// step-specific buttons (e.g., Discover) before the Remove button.
/// Use <see cref="GetItems"/> / <see cref="SetItems"/> / <see cref="AddItem"/> to access the list.
/// </remarks>
public class FolderListEditor : UserControl
{
    private Label _infoLabel = null!;
    private ToolStrip _toolStrip = null!;
    private ToolStripButton _addButton = null!;
    private ToolStripButton _removeButton = null!;
    private ListBox _listBox = null!;
    private ContextMenuStrip _contextMenu = null!;
    private ToolStripMenuItem _ctxAdd = null!;
    private ToolStripMenuItem _ctxRemove = null!;

    private FolderBrowseDialogType _browseType;
    private Func<string, bool>? _validate;
    private string? _browseDialogTitle;

    public FolderListEditor()
    {
        BackColor = Color.White;
        BuildContent();
    }

    /// <summary>
    /// Configures the editor with step-specific runtime data.
    /// Must be called once before the editor is shown.
    /// </summary>
    /// <param name="labelText">Descriptive text shown above the list.</param>
    /// <param name="browseType">Which dialog to open when the user clicks Add.</param>
    /// <param name="validate">
    /// Optional callback invoked with the selected path before adding it.
    /// Return <c>false</c> to reject the item. Duplicate paths are always rejected regardless.
    /// </param>
    /// <param name="browseDialogTitle">
    /// Optional title/description shown in the browse dialog. When null, a generic title is used.
    /// </param>
    public void Initialize(string labelText, FolderBrowseDialogType browseType, Func<string, bool>? validate = null,
        string? browseDialogTitle = null)
    {
        _browseType = browseType;
        _validate = validate;
        _browseDialogTitle = browseDialogTitle;

        _infoLabel.Text = labelText;
        _addButton.ToolTipText = browseType == FolderBrowseDialogType.ExecutableFile ? "Add Launcher…" : "Add Folder…";
        _ctxAdd.Text = browseType == FolderBrowseDialogType.ExecutableFile ? "Add Launcher…" : "Add Folder…";

        WrappingLabelHelper.UpdateHeight(this, _infoLabel);
    }

    /// <summary>Returns all current items in the list.</summary>
    public IReadOnlyList<string> GetItems() => _listBox.Items.Cast<string>().ToList();

    /// <summary>Replaces all items in the list with <paramref name="items"/>.</summary>
    public void SetItems(IEnumerable<string> items)
    {
        _listBox.Items.Clear();
        foreach (var item in items)
            _listBox.Items.Add(item);
        UpdateButtons();
    }

    /// <summary>
    /// Adds <paramref name="path"/> to the list if it is not already present (case-insensitive comparison).
    /// Used by step auto-detection logic.
    /// </summary>
    public void AddItem(string path)
    {
        if (!ContainsPath(path))
            _listBox.Items.Add(path);
    }

    /// <summary>
    /// Inserts a step-specific toolbar button immediately before the Remove button.
    /// Use this to add a Discover button or similar step-specific action.
    /// </summary>
    public void AddExtraToolbarButton(ToolStripButton button)
    {
        var removeIndex = _toolStrip.Items.IndexOf(_removeButton);
        _toolStrip.Items.Insert(removeIndex, button);
    }

    /// <summary>
    /// Inserts a step-specific context menu item immediately before the Remove item.
    /// </summary>
    /// <param name="item">The menu item to insert.</param>
    /// <param name="target">
    /// Controls when the item is visible: <see cref="ContextMenuTarget.EmptySpace"/> shows it
    /// only when no list item is selected; <see cref="ContextMenuTarget.SelectedItem"/> shows it
    /// only when a list item is selected.
    /// </param>
    public void AddExtraContextMenuItem(ToolStripMenuItem item, ContextMenuTarget target = ContextMenuTarget.EmptySpace)
    {
        item.Tag = target;
        var removeIndex = _contextMenu.Items.IndexOf(_ctxRemove);
        _contextMenu.Items.Insert(removeIndex, item);
    }

    private void BuildContent()
    {
        SuspendLayout();
        Dock = DockStyle.Fill;

        _infoLabel = new Label
        {
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 8)
        };
        Resize += (_, _) => WrappingLabelHelper.UpdateHeight(this, _infoLabel);

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

        _listBox = new ListBox
        {
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.One,
            IntegralHeight = false
        };
        _listBox.SelectedIndexChanged += (_, _) => UpdateButtons();
        _listBox.MouseDown += OnMouseDown;
        _listBox.KeyDown += OnKeyDown;

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
        _listBox.ContextMenuStrip = _contextMenu;

        // Fill first, then Top items (last added = topmost in DockStyle.Top stack).
        Controls.Add(_listBox);
        Controls.Add(_toolStrip);
        Controls.Add(_infoLabel);
        ResumeLayout(false);
    }

    private bool ContainsPath(string path) =>
        _listBox.Items.Cast<string>().Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    private void OnAddClick(object? sender, EventArgs e)
    {
        var path = BrowseForPath();
        if (path == null)
            return;
        if (ContainsPath(path))
            return;
        if (_validate != null && !_validate(path))
            return;
        _listBox.Items.Add(path);
    }

    private string? BrowseForPath()
    {
        switch (_browseType)
        {
            case FolderBrowseDialogType.FolderWithCreate:
            {
                using var dlg = new FolderBrowserDialog();
                dlg.Description = _browseDialogTitle ?? "Select a folder";
                dlg.UseDescriptionForTitle = true;
                dlg.ShowNewFolderButton = true;
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
            }
            case FolderBrowseDialogType.FolderWithoutCreate:
            {
                using var dlg = new FolderBrowserDialog();
                dlg.Description = _browseDialogTitle ?? "Select a folder";
                dlg.UseDescriptionForTitle = true;
                dlg.ShowNewFolderButton = false;
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
            }
            case FolderBrowseDialogType.ExecutableFile:
            {
                using var dlg = new OpenFileDialog();
                dlg.Title = _browseDialogTitle ?? "Select Executable";
                dlg.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
                dlg.CheckFileExists = true;
                FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
            }
            default:
                return null;
        }
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_listBox.SelectedIndex >= 0)
            _listBox.Items.RemoveAt(_listBox.SelectedIndex);
    }

    private void UpdateButtons()
    {
        _removeButton.Enabled = _listBox.SelectedIndex >= 0;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            _listBox.SelectedIndex = _listBox.IndexFromPoint(e.Location);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && _listBox.SelectedIndex >= 0)
            OnRemoveClick(sender, e);
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        var hasSelection = _listBox.SelectedIndex >= 0;
        foreach (ToolStripItem item in _contextMenu.Items)
        {
            if (item == _ctxAdd)
                item.Visible = !hasSelection;
            else if (item == _ctxRemove)
                item.Visible = hasSelection;
            else if (item.Tag is ContextMenuTarget target)
                item.Visible = target == ContextMenuTarget.SelectedItem ? hasSelection : !hasSelection;
            else
                item.Visible = !hasSelection; // default: treat untagged extra items as EmptySpace
        }
    }

}
