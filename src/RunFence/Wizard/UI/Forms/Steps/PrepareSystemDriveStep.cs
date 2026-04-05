using System.Security.Principal;
using RunFence.Acl;
using RunFence.Core;
using RunFence.UI;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step that lists fixed drives (excluding the system drive) and lets the user
/// choose a target SID per drive for ACL replacement by <see cref="Acl.DriveAclReplacer"/>.
/// Only drives where replaceable ACEs exist and the root is accessible are shown.
/// </summary>
public class PrepareSystemDriveStep : WizardStepPage
{
    private readonly Action<List<(string DrivePath, string TargetSid)>> _setDriveSelections;
    private readonly IReadOnlyDictionary<string, string> _sidNames;
    private readonly DriveAclReplacer _driveAclReplacer;

    private Label _infoLabel = null!;
    private DataGridView _driveGrid = null!;

    // Column indices
    private const int ColEnabled = 0;
    private const int ColDrive = 1;
    private const int ColSize = 2;
    private const int ColTarget = 3;

    // Per-row: drive path → list of (displayName, sid)
    private readonly Dictionary<string, List<(string Display, string Sid)>> _rowTargets = new(StringComparer.OrdinalIgnoreCase);

    public PrepareSystemDriveStep(
        Action<List<(string DrivePath, string TargetSid)>> setDriveSelections,
        IReadOnlyDictionary<string, string> sidNames,
        DriveAclReplacer driveAclReplacer)
    {
        _setDriveSelections = setDriveSelections;
        _sidNames = sidNames;
        _driveAclReplacer = driveAclReplacer;
        BuildContent();
    }

    public override string StepTitle => "Prepare System Drives";

    public override bool CanProceed => _driveGrid.Rows.Cast<DataGridViewRow>()
        .Any(r => r.Cells[ColEnabled] is DataGridViewCheckBoxCell { Value: true });

    public override void OnActivated() => PopulateDrives();

    public override string? Validate()
    {
        if (_driveGrid.IsCurrentCellDirty)
            _driveGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        bool anySelected = _driveGrid.Rows.Cast<DataGridViewRow>()
            .Any(r => r.Cells[ColEnabled] is DataGridViewCheckBoxCell { Value: true });
        if (!anySelected)
            return "Please select at least one drive to process, or click Back to skip this step.";
        return null;
    }

    public override void Collect()
    {
        if (_driveGrid.IsCurrentCellDirty)
            _driveGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);

        var selections = new List<(string, string)>();
        foreach (DataGridViewRow row in _driveGrid.Rows)
        {
            if (row.Cells[ColEnabled] is not DataGridViewCheckBoxCell { Value: true })
                continue;

            var drivePath = row.Tag as string ?? string.Empty;
            var selectedDisplay = row.Cells[ColTarget].Value as string ?? string.Empty;

            if (string.IsNullOrEmpty(drivePath))
                continue;
            if (!_rowTargets.TryGetValue(drivePath, out var targets))
                continue;

            var match = targets.FirstOrDefault(t => t.Display == selectedDisplay);
            if (!string.IsNullOrEmpty(match.Sid))
                selections.Add((drivePath, match.Sid));
        }

        _setDriveSelections(selections);
    }

    private void BuildContent()
    {
        SuspendLayout();
        Padding = new Padding(8);

        _infoLabel = new Label
        {
            Text = "Select which fixed drives to prepare. Broad user-group entries (Users, Authenticated Users, " +
                   "Everyone) on each drive root are replaced with ACEs for the chosen account — child items " +
                   "inherit the updated permissions automatically. A backup will be saved first.\r\n\r\n" +
                   "Note: other setup templates assume this step has been run. Without it, isolated accounts " +
                   "may still be able to read files on data drives, so full isolation may not be achieved.",
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(0x33, 0x33, 0x33),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 8)
        };
        TrackWrappingLabel(_infoLabel);

        _driveGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5f)
        };

        var checkCol = new DataGridViewCheckBoxColumn
        {
            HeaderText = string.Empty,
            Width = 28,
            MinimumWidth = 28,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        var driveCol = new DataGridViewTextBoxColumn
        {
            HeaderText = "Drive",
            Width = 100,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        var sizeCol = new DataGridViewTextBoxColumn
        {
            HeaderText = "Size",
            Width = 80,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        var targetCol = new DataGridViewComboBoxColumn
        {
            HeaderText = "Replace with",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        _driveGrid.Columns.AddRange(checkCol, driveCol, sizeCol, targetCol);

        _driveGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_driveGrid.IsCurrentCellDirty)
                _driveGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _driveGrid.CellValueChanged += (_, e) =>
        {
            if (e.ColumnIndex == ColEnabled)
                NotifyCanProceedChanged();
        };

        DataGridViewComboHelper.EnableComboOpenOnFirstClick(_driveGrid);

        // Fill control first (lower z-order = docked last), label second (higher z-order = docked first → Top)
        Controls.Add(_driveGrid);
        Controls.Add(_infoLabel);
        ResumeLayout(false);
    }

    private void PopulateDrives()
    {
        _driveGrid.Rows.Clear();
        _rowTargets.Clear();

        var adminsSid = new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid, null).Value;
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();

        var adminDisplay = BuildTargetDisplay(adminsSid, "Administrators group");
        var interactiveDisplay = interactiveSid != null && !SidResolutionHelper.IsCurrentUserInteractive()
            ? BuildTargetDisplay(interactiveSid, "Interactive user")
            : null;

        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

        foreach (var drive in DriveInfo.GetDrives().Where(d =>
                     d.DriveType == DriveType.Fixed &&
                     !string.Equals(d.RootDirectory.FullName, systemDrive, StringComparison.OrdinalIgnoreCase)))
        {
            var drivePath = drive.RootDirectory.FullName;

            // Skip drives where root ACL is inaccessible or has no replaceable broad ACEs
            if (!_driveAclReplacer.HasReplaceableBroadAces(drivePath))
                continue;

            string sizeText;
            try
            {
                sizeText = FormatSize(drive.TotalSize);
            }
            catch
            {
                sizeText = "N/A";
            }

            string fsType;
            try
            {
                fsType = drive.DriveFormat;
            }
            catch
            {
                fsType = string.Empty;
            }

            var targets = new List<(string Display, string Sid)> { (adminDisplay, adminsSid) };
            if (interactiveSid != null && interactiveDisplay != null)
                targets.Add((interactiveDisplay, interactiveSid));

            _rowTargets[drivePath] = targets;

            var driveLabel = string.IsNullOrEmpty(fsType)
                ? drivePath
                : $"{drivePath}  {fsType}";

            var row = new DataGridViewRow();
            row.CreateCells(_driveGrid);
            row.Cells[ColEnabled].Value = false;
            row.Cells[ColDrive].Value = driveLabel;
            row.Cells[ColSize].Value = sizeText;

            var combo = (DataGridViewComboBoxCell)row.Cells[ColTarget];
            foreach (var (display, _) in targets)
                combo.Items.Add(display);
            combo.Value = targets[0].Display;

            row.Tag = drivePath;
            _driveGrid.Rows.Add(row);
        }

        NotifyCanProceedChanged();
    }

    private string BuildTargetDisplay(string sid, string fallback)
    {
        if (_sidNames.TryGetValue(sid, out var name) && !string.IsNullOrEmpty(name))
            return name;
        return fallback;
    }

    private static string FormatSize(long bytes)
    {
        const long gb = 1024L * 1024 * 1024;
        const long mb = 1024L * 1024;
        return bytes >= gb ? $"{bytes / gb} GB"
            : bytes >= mb ? $"{bytes / mb} MB"
            : $"{bytes} B";
    }
}