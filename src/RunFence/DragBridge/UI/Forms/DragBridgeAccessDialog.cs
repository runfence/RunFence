using System.Drawing.Drawing2D;

namespace RunFence.DragBridge.UI.Forms;

public partial class DragBridgeAccessDialog : Form
{
    private const long LargeSizeWarningBytes = 100L * 1024 * 1024; // 100 MB

    public DragBridgeAccessAction? ChosenAction { get; private set; }

    public DragBridgeAccessDialog(string targetDisplayName, IReadOnlyList<string> inaccessiblePaths, long totalSizeBytes)
    {
        InitializeComponent();
        _headerLabel.Text = $"The target account \"{targetDisplayName}\" cannot access {inaccessiblePaths.Count} file(s):";
        foreach (var p in inaccessiblePaths)
            _fileListBox.Items.Add(p);

        if (totalSizeBytes > LargeSizeWarningBytes)
        {
            var mb = totalSizeBytes / (1024.0 * 1024.0);
            _sizeWarningLabel.Text = $"Warning: Total size is {mb:F0} MB. Copying to Temp may take time.";
            _sizeWarningLabel.Visible = true;
        }

        _tooltip.SetToolTip(_grantButton, "Adds file system ACL entries to grant the target account read access to the files.");
        _tooltip.SetToolTip(_copyButton, "Copies the inaccessible files to a shared temp folder accessible by the target account.");
        _tooltip.SetToolTip(_copyWholeFolderButton, "Grants the target account read and execute access to the entire folder(s) containing the inaccessible files, including all contents.");
        _tooltip.SetToolTip(_cancelButton, "Cancel the paste operation.");

        using (var shieldIcon = new Icon(SystemIcons.Shield, 16, 16))
            _grantButton.Image = shieldIcon.ToBitmap();
        _grantButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _grantButton.ImageAlign = ContentAlignment.MiddleLeft;

        _copyWholeFolderButton.Image = CreateFolderIcon();
        _copyWholeFolderButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _copyWholeFolderButton.ImageAlign = ContentAlignment.MiddleLeft;

        _copyButton.Image = CreateCopyIcon();
        _copyButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _copyButton.ImageAlign = ContentAlignment.MiddleLeft;
    }

    private static Bitmap CreateFolderIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(0, 100, 180), 1.5f);
        // Folder tab (top-left bump)
        g.DrawLines(pen, new PointF[] { new(1, 7), new(1, 5), new(5, 5), new(6, 7) });
        // Folder body
        g.DrawRectangle(pen, 1, 7, 13, 7);
        return bmp;
    }

    private static Bitmap CreateCopyIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(0, 100, 180), 1.5f);
        // Back document (offset)
        g.DrawRectangle(pen, 4, 3, 9, 11);
        // Front document (overlapping)
        g.DrawRectangle(pen, 2, 1, 9, 11);
        return bmp;
    }

    private void OnGrantClick(object? sender, EventArgs e)
    {
        ChosenAction = DragBridgeAccessAction.GrantAccess;
        DialogResult = DialogResult.OK;
    }

    private void OnCopyClick(object? sender, EventArgs e)
    {
        ChosenAction = DragBridgeAccessAction.CopyToTemp;
        DialogResult = DialogResult.OK;
    }

    private void OnCopyWholeFolderClick(object? sender, EventArgs e)
    {
        ChosenAction = DragBridgeAccessAction.GrantFolderAccess;
        DialogResult = DialogResult.OK;
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        ChosenAction = null;
        DialogResult = DialogResult.Cancel;
    }
}