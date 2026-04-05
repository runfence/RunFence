using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.SidMigration.UI.Forms;

/// <summary>
/// UserControl for Step 7 (In-App Data Migration) of SidMigrationDialog.
/// Displays the list of in-app SID changes and provides an Apply button.
/// </summary>
public partial class SidMigrationInAppStep : UserControl
{
    private readonly InAppMigrationHandler _inAppMigrationHandler;
    private readonly SessionContext _session;
    private readonly List<SidMigrationMapping> _filteredMappings;
    private readonly List<string> _filteredDeletes;
    private readonly ISidResolver _sidResolver;

    /// <summary>Raised when the in-app migration is successfully applied.</summary>
    public event EventHandler? MigrationApplied;

    public SidMigrationInAppStep(
        InAppMigrationHandler inAppMigrationHandler,
        SessionContext session,
        List<SidMigrationMapping> filteredMappings,
        List<string> filteredDeletes,
        ISidResolver sidResolver)
    {
        _inAppMigrationHandler = inAppMigrationHandler;
        _session = session;
        _filteredMappings = filteredMappings;
        _filteredDeletes = filteredDeletes;
        _sidResolver = sidResolver;

        InitializeComponent();
        PopulateList();
    }

    private void PopulateList()
    {
        var hasActions = _filteredMappings.Count > 0 || _filteredDeletes.Count > 0;

        if (hasActions)
        {
            var actions = new List<string>();
            if (_filteredMappings.Count > 0)
                actions.Add($"{_filteredMappings.Count} SID(s) to migrate");
            if (_filteredDeletes.Count > 0)
                actions.Add($"{_filteredDeletes.Count} SID(s) to delete");
            _summaryLabel.Text = $"Actions: {string.Join(", ", actions)}";
        }
        else
        {
            _summaryLabel.Text = "No in-app references found for the selected SIDs.";
        }

        foreach (var m in _filteredMappings)
            _listBox.Items.Add($"[Migrate] {m.Username}: {m.OldSid} \u2192 {m.NewSid}");
        foreach (var sid in _filteredDeletes)
        {
            var name = _sidResolver.TryResolveNameFromRegistry(sid) ?? sid;
            _listBox.Items.Add($"[Delete]  {name}: {sid}");
        }

        _applyButton.Visible = hasActions;
        _resultLabel.Visible = hasActions;
    }

    private void OnApplyClick(object? sender, EventArgs e)
    {
        var validationError = _inAppMigrationHandler.Validate(_filteredMappings, _filteredDeletes);
        if (validationError != null)
        {
            MessageBox.Show(validationError, "Validation Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var (messages, success, saveError) = _inAppMigrationHandler.Apply(_filteredMappings, _filteredDeletes, _session);

        if (!success)
        {
            _resultLabel.ForeColor = Color.Red;
            _resultLabel.Text = messages.Count > 0 ? messages[0] : "Migration failed.";
            return;
        }

        _applyButton.Enabled = false;
        MigrationApplied?.Invoke(this, EventArgs.Empty);

        if (saveError != null)
        {
            _resultLabel.ForeColor = Color.OrangeRed;
            _resultLabel.Text = string.Join("\n", messages) +
                                $"\nSave failed: {saveError}. Please restart the application.";
        }
        else
        {
            _resultLabel.ForeColor = Color.DarkGreen;
            _resultLabel.Text = string.Join("\n", messages);
        }
    }
}