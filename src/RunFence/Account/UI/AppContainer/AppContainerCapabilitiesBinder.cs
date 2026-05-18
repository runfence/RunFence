using System.ComponentModel;
using RunFence.Core.Models;
using RunFence.UI;

namespace RunFence.Account.UI.AppContainer;

public class AppContainerCapabilitiesBinder(IAppContainerEditDialogNotifier notifier)
{
    private static readonly (string Name, string Clsid)[] KnownComObjects =
    [
        ("Shell.Application", "{13709620-C279-11CE-A49E-444553540000}"),
        ("WScript.Shell", "{F935DC22-1CF0-11D0-ADB9-00C04FD58A0B}"),
        ("Scripting.FileSystemObject", "{0D43FE01-F093-11CF-8940-00A0C9054B29}"),
    ];

    private static readonly (string Name, string Sid, bool DefaultOn)[] KnownCapabilities =
    [
        ("internetClient", "S-1-15-3-1", true),
        ("internetClientServer", "S-1-15-3-2", true),
        ("privateNetworkClientServer", "S-1-15-3-3", true),
        ("picturesLibrary", "S-1-15-3-4", false),
        ("videosLibrary", "S-1-15-3-5", false),
        ("musicLibrary", "S-1-15-3-6", false),
        ("documentsLibrary", "S-1-15-3-7", false),
        ("enterpriseAuthentication", "S-1-15-3-8", false),
        ("sharedUserCertificates", "S-1-15-3-9", false),
        ("removableStorage", "S-1-15-3-10", false),
    ];

    private record ComboItemData(string Name, string Clsid)
    {
        public override string ToString() => Name;
    }

    public CheckBox[] InitializeCapabilityRows(FlowLayoutPanel capFlow, CheckBox loopbackCheckBox)
    {
        var capabilityCheckBoxes = new CheckBox[KnownCapabilities.Length];
        for (int i = 0; i < KnownCapabilities.Length; i++)
        {
            var capability = KnownCapabilities[i];
            var capabilityCheckBox = new CheckBox
            {
                Text = capability.Name,
                Width = 202,
                AutoSize = false,
                Tag = capability.Sid,
                Margin = new Padding(2),
            };
            capabilityCheckBoxes[i] = capabilityCheckBox;
            capFlow.Controls.Add(capabilityCheckBox);
        }

        capFlow.Controls.Add(loopbackCheckBox);
        return capabilityCheckBoxes;
    }

    public void WireComToolbar(
        ToolStrip comToolStrip,
        ListBox comCustomListBox,
        IContainer? components,
        IWin32Window owner,
        Action<string> setValidationCaption)
    {
        var addButton = new ToolStripButton
        {
            Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 16),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Add CLSID…"
        };
        var browseButton = new ToolStripButton
        {
            Image = UiIconFactory.CreateToolbarIcon("\U0001F50D", Color.FromArgb(0x33, 0x66, 0x99), 16),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Browse registered COM objects…"
        };
        var removeButton = new ToolStripButton
        {
            Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 16),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Remove selected",
            Enabled = false
        };

        comToolStrip.Items.AddRange(addButton, browseButton, new ToolStripSeparator(), removeButton);

        comCustomListBox.SelectedIndexChanged += (_, _) =>
            removeButton.Enabled = comCustomListBox.SelectedIndex >= 0;

        var contextMenu = components != null
            ? new ContextMenuStrip(components)
            : new ContextMenuStrip();
        var addMenuItem = new ToolStripMenuItem("Add CLSID…")
        {
            Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 16)
        };
        var browseMenuItem = new ToolStripMenuItem("Browse registered COM objects…")
        {
            Image = UiIconFactory.CreateToolbarIcon("\U0001F50D", Color.FromArgb(0x33, 0x66, 0x99), 16)
        };
        var removeMenuItem = new ToolStripMenuItem("Remove")
        {
            Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 16)
        };
        contextMenu.Items.AddRange(addMenuItem, browseMenuItem, removeMenuItem);

        contextMenu.Opening += (_, _) =>
        {
            var point = comCustomListBox.PointToClient(Cursor.Position);
            var hitIndex = comCustomListBox.IndexFromPoint(point);
            var onItem = hitIndex >= 0 && hitIndex < comCustomListBox.Items.Count;
            if (onItem)
                comCustomListBox.SelectedIndex = hitIndex;

            addMenuItem.Visible = !onItem;
            browseMenuItem.Visible = !onItem;
            removeMenuItem.Visible = onItem;
        };
        comCustomListBox.ContextMenuStrip = contextMenu;

        void AddClsid()
        {
            var result = ShowAddClsidPrompt(owner);
            if (result == null)
                return;

            if (!ClsidValidator.IsValid(result))
            {
                setValidationCaption("Invalid CLSID");
                notifier.ShowValidationWarning(
                    owner,
                    "Enter a valid CLSID in the form {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}.");
                return;
            }

            if (!comCustomListBox.Items.Cast<string>().Any(item =>
                    string.Equals(item, result, StringComparison.OrdinalIgnoreCase)))
            {
                comCustomListBox.Items.Add(result);
            }
        }

        void Browse()
        {
            using var dialog = new ComBrowserDialog();
            if (dialog.ShowDialog(owner) == DialogResult.OK && dialog.SelectedAppId != null)
            {
                var clsid = dialog.SelectedAppId;
                if (!comCustomListBox.Items.Cast<string>().Any(item =>
                        string.Equals(item, clsid, StringComparison.OrdinalIgnoreCase)))
                {
                    comCustomListBox.Items.Add(clsid);
                }
            }
        }

        void Remove()
        {
            if (comCustomListBox.SelectedIndex >= 0)
                comCustomListBox.Items.RemoveAt(comCustomListBox.SelectedIndex);
        }

        addButton.Click += (_, _) => AddClsid();
        browseButton.Click += (_, _) => Browse();
        removeButton.Click += (_, _) => Remove();
        addMenuItem.Click += (_, _) => AddClsid();
        browseMenuItem.Click += (_, _) => Browse();
        removeMenuItem.Click += (_, _) => Remove();
    }

    public void PopulateFromExisting(
        AppContainerEntry existing,
        TextBox displayNameBox,
        TextBox profileNameBox,
        TextBox sidBox,
        CheckBox[] capabilityCheckBoxes,
        CheckBox loopbackCheckBox,
        CheckBox ephemeralCheckBox,
        ListBox comCustomListBox)
    {
        displayNameBox.Text = existing.DisplayName;
        profileNameBox.Text = existing.Name;
        sidBox.Text = !string.IsNullOrEmpty(existing.Sid) ? existing.Sid : "(unavailable)";
        loopbackCheckBox.Checked = existing.EnableLoopback;
        ephemeralCheckBox.Checked = existing.IsEphemeral;

        var existingCapabilities = existing.Capabilities ?? [];
        for (int i = 0; i < KnownCapabilities.Length; i++)
            capabilityCheckBoxes[i].Checked = existingCapabilities.Contains(KnownCapabilities[i].Sid);

        foreach (var clsid in existing.ComAccessClsids ?? [])
            comCustomListBox.Items.Add(clsid);
    }

    public void ApplyDefaultCapabilities(CheckBox[] capabilityCheckBoxes, CheckBox loopbackCheckBox)
    {
        for (int i = 0; i < KnownCapabilities.Length; i++)
            capabilityCheckBoxes[i].Checked = KnownCapabilities[i].DefaultOn;

        loopbackCheckBox.Checked = false;
    }

    public void RefreshProfileNamePreview(
        AppContainerEntry? existing,
        TextBox displayNameBox,
        TextBox profileNameBox,
        CheckBox ephemeralCheckBox)
    {
        if (existing != null)
            return;

        var isEphemeral = ephemeralCheckBox.Checked;
        profileNameBox.ReadOnly = isEphemeral;
        profileNameBox.BackColor = isEphemeral ? SystemColors.Control : SystemColors.Window;
        profileNameBox.Text = isEphemeral
            ? "(auto-generated)"
            : AppContainerDialogStateAssembler.GenerateProfileName(displayNameBox.Text);
    }

    private static string? ShowAddClsidPrompt(IWin32Window owner)
    {
        using var dialog = new Form
        {
            Text = "Add COM Object",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(380, 90),
            Padding = new Padding(10),
        };

        var comboBox = new ComboBox
        {
            Left = 10,
            Top = 10,
            Width = 360,
            Height = 24,
            DropDownStyle = ComboBoxStyle.DropDown,
        };

        foreach (var comObject in KnownComObjects)
            comboBox.Items.Add(new ComboItemData(comObject.Name, comObject.Clsid));

        comboBox.TextChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is ComboItemData item && comboBox.Text != item.ToString())
                comboBox.SelectedIndex = -1;
        };

        var okButton = new Button
        {
            Text = "Add",
            DialogResult = DialogResult.OK,
            Left = 210,
            Top = 52,
            Width = 75,
            Height = 26,
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Left = 295,
            Top = 52,
            Width = 75,
            Height = 26,
        };

        dialog.Controls.AddRange(comboBox, okButton, cancelButton);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return null;

        if (comboBox.SelectedItem is ComboItemData selected)
            return selected.Clsid;

        var text = comboBox.Text.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
