#nullable disable
using System.ComponentModel;

namespace RunFence.Account.UI.AppContainer;

partial class ComBrowserDialog
{
    private IContainer components = null;

    private TextBox _filterBox;
    private ListBox _list;
    private Button _okButton;
    private Button _cancelButton;
    private FlowLayoutPanel _buttonPanel;

    public ComBrowserDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _filterBox = new TextBox();
        _list = new ListBox();
        _okButton = new Button();
        _cancelButton = new Button();
        _buttonPanel = new FlowLayoutPanel();

        _buttonPanel.SuspendLayout();
        SuspendLayout();

        // _filterBox
        _filterBox.Dock = DockStyle.Top;
        _filterBox.PlaceholderText = "Filter by name or AppID\u2026";
        _filterBox.Margin = new Padding(0);

        // _list
        _list.Dock = DockStyle.Fill;
        _list.HorizontalScrollbar = true;
        _list.DoubleClick += OnListDoubleClick;

        // _okButton
        _okButton.Text = "Select";
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Width = 80;
        _okButton.Height = 26;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Width = 80;
        _cancelButton.Height = 26;

        // _buttonPanel
        _buttonPanel.Dock = DockStyle.Bottom;
        _buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        _buttonPanel.AutoSize = true;
        _buttonPanel.Padding = new Padding(4, 6, 4, 6);
        _buttonPanel.Controls.Add(_cancelButton);
        _buttonPanel.Controls.Add(_okButton);

        // ComBrowserDialog
        Text = "Browse COM Objects";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(520, 380);
        Size = new Size(580, 460);
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.Add(_list);
        Controls.Add(_filterBox);
        Controls.Add(_buttonPanel);

        _buttonPanel.ResumeLayout(false);
        _buttonPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
