#nullable disable

using System.ComponentModel;

namespace RunFence.DragBridge.UI.Forms;

partial class DragBridgeSection
{
    private IContainer components = null;

    private GroupBox _groupBox;
    private CheckBox _enableCheckBox;
    private Label _hotkeyLabel;
    private TextBox _hotkeyBox;

    public DragBridgeSection()
    {
        InitializeComponent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _groupBox = new GroupBox();
        _enableCheckBox = new CheckBox();
        _hotkeyLabel = new Label();
        _hotkeyBox = new TextBox();

        _groupBox.SuspendLayout();
        SuspendLayout();

        // _groupBox
        _groupBox.Text = "Drag Bridge (Cross-Account Drag & Drop)";
        _groupBox.FlatStyle = FlatStyle.System;
        _groupBox.Dock = DockStyle.Fill;
        _groupBox.Controls.AddRange(new Control[] { _enableCheckBox, _hotkeyLabel, _hotkeyBox });

        // _enableCheckBox
        _enableCheckBox.Text = "Enable";
        _enableCheckBox.Location = new Point(15, 24);
        _enableCheckBox.AutoSize = true;
        _enableCheckBox.CheckedChanged += OnEnableCheckedChanged;

        // _hotkeyLabel
        _hotkeyLabel.Text = "Hotkey:";
        _hotkeyLabel.Location = new Point(88, 27);
        _hotkeyLabel.AutoSize = true;

        // _hotkeyBox
        _hotkeyBox.Location = new Point(148, 23);
        _hotkeyBox.Size = new Size(105, 23);
        _hotkeyBox.ReadOnly = true;
        _hotkeyBox.Enabled = false;
        _hotkeyBox.KeyDown += OnHotkeyKeyDown;

        // DragBridgeSection
        AutoScaleMode = AutoScaleMode.Inherit;
        Margin = Padding.Empty;
        Height = 55;
        Controls.Add(_groupBox);

        _groupBox.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
