#nullable disable

using System.ComponentModel;

namespace RunFence.Wizard.UI.Forms.Steps;

partial class AppPathBrowseControl
{
    private IContainer components = null;

    private TextBox _pathTextBox;
    private Button _browseButton;
    private Button _discoverButton;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _pathTextBox = new TextBox();
        _browseButton = new Button();
        _discoverButton = new Button();

        SuspendLayout();

        _pathTextBox.Dock = DockStyle.Fill;
        _pathTextBox.TextChanged += OnPathTextChanged;

        _browseButton.Text = "Browse\u2026";
        _browseButton.Width = 72;
        _browseButton.Dock = DockStyle.Right;
        _browseButton.FlatStyle = FlatStyle.System;
        _browseButton.Click += OnBrowseClick;

        _discoverButton.Text = "Discover\u2026";
        _discoverButton.Width = 88;
        _discoverButton.Dock = DockStyle.Right;
        _discoverButton.FlatStyle = FlatStyle.System;
        _discoverButton.Click += OnDiscoverClick;

        // Dock=Right: first added = rightmost. Add Discover first (rightmost), Browse second (left of Discover), Fill textbox last.
        Controls.Add(_discoverButton);
        Controls.Add(_browseButton);
        Controls.Add(_pathTextBox);

        // AutoScaleMode.Inherit: DPI scaling delegated to the parent wizard step.
        AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Top;
        ResumeLayout(false);
        PerformLayout();
    }
}
