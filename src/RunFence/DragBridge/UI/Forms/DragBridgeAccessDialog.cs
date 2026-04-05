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
        _headerLabel.Size = new Size(546, 36);
        _fileListBox.Size = new Size(546, 120);
        foreach (var p in inaccessiblePaths)
            _fileListBox.Items.Add(p);

        var yStart = 182;

        if (totalSizeBytes > LargeSizeWarningBytes)
        {
            var mb = totalSizeBytes / (1024.0 * 1024.0);
            _sizeWarningLabel.Text = $"Warning: Total size is {mb:F0} MB. Copying may take time.";
            _sizeWarningLabel.Size = new Size(546, 20);
            _sizeWarningLabel.Visible = true;
            yStart += 24;
        }

        _grantButton.Location = new Point(12, yStart);
        _copyButton.Location = new Point(152, yStart);
        _copyWholeFolderButton.Location = new Point(292, yStart);
        _cancelButton.Location = new Point(570 - 12 - _cancelButton.Width, yStart);
        ClientSize = new Size(570, yStart + 34 + 12);

        using (var shieldIcon = new Icon(SystemIcons.Shield, 16, 16))
            _grantButton.Image = shieldIcon.ToBitmap();
        _grantButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _grantButton.ImageAlign = ContentAlignment.MiddleLeft;

        _copyButton.Image = CreateCopyIcon();
        _copyButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _copyButton.ImageAlign = ContentAlignment.MiddleLeft;

        _copyWholeFolderButton.Image = CreateCopyIcon();
        _copyWholeFolderButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _copyWholeFolderButton.ImageAlign = ContentAlignment.MiddleLeft;
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
        ChosenAction = DragBridgeAccessAction.CopyToTempWholeFolder;
        DialogResult = DialogResult.OK;
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        ChosenAction = null;
        DialogResult = DialogResult.Cancel;
    }
}