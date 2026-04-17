using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.PrefTrans.UI.Forms;
using RunFence.UI.Forms;

namespace RunFence.Account.UI;

public class AccountImportUIHandler(IAccountImportHandler importHandler)
{
    private DataGridView _grid = null!;
    private ToolStripButton _importButton = null!;
    private bool _importColumnVisible;

    public void Initialize(DataGridView grid, ToolStripButton importButton)
    {
        _grid = grid;
        _importButton = importButton;
    }

    public async Task HandleImportClickAsync(
        AppDatabase db,
        CredentialStore store,
        ProtectedBuffer key,
        Form? parent,
        Control owner,
        OperationGuard operationGuard,
        Action<string> setStatus,
        Action<bool> setControlsEnabled,
        Action updateButtonState,
        Action<string> savePrefsPath)
    {
        if (!_importColumnVisible)
        {
            SetImportMode(true, updateButtonState);

            if (_grid.SelectedRows.Count > 0)
            {
                var selectedRow = _grid.SelectedRows[0];
                if (selectedRow.Tag is AccountRow { CanImport: true })
                    selectedRow.Cells["Import"].Value = true;
            }

            return;
        }

        var selectedAccounts = new List<AccountRow>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is AccountRow { CanImport: true } ar && row.Cells["Import"].Value is true)
                selectedAccounts.Add(ar);
        }

        if (selectedAccounts.Count == 0)
        {
            SetImportMode(false, updateButtonState);
            return;
        }

        await ImportToAccountsAsync(selectedAccounts, db, store, key, parent, owner, operationGuard, setStatus, setControlsEnabled, updateButtonState, savePrefsPath);

        SetImportMode(false, updateButtonState);
        foreach (DataGridViewRow row in _grid.Rows)
            row.Cells["Import"].Value = false;
    }

    private async Task ImportToAccountsAsync(
        List<AccountRow> accounts,
        AppDatabase db,
        CredentialStore store,
        ProtectedBuffer key,
        Form? parent,
        Control owner,
        OperationGuard operationGuard,
        Action<string> setStatus,
        Action<bool> setControlsEnabled,
        Action updateButtonState,
        Action<string> savePrefsPath)
    {
        setControlsEnabled(false);
        operationGuard.Begin(owner);

        try
        {
            var importAccounts = accounts
                .Select(a => new ImportAccount(
                    a.Credential ?? new CredentialEntry { Sid = a.Sid }, a.Username))
                .ToList();

            string? selectedFile;
            using (var dlg = new OpenFileDialog())
            {
                DesktopSettingsImportDialog.Setup(dlg, db.LastPrefsFilePath);
                if (dlg.ShowDialog(parent) != DialogResult.OK)
                    return;
                selectedFile = dlg.FileName;
            }

            var logForm = new ImportProgressForm(parent); // intentionally no using — form closes/disposes itself when user clicks OK

            var settingsPath = await importHandler.RunImportAsync(
                importAccounts, store, key,
                () => selectedFile,
                logForm.AppendLog,
                logForm.EnableOkButton,
                text =>
                {
                    if (!owner.IsDisposed)
                        setStatus(text);
                },
                db);

            if (settingsPath != null && !owner.IsDisposed)
                savePrefsPath(settingsPath);
        }
        finally
        {
            operationGuard.End(owner);
            if (!owner.IsDisposed)
            {
                setControlsEnabled(true);
                updateButtonState();
            }
        }
    }

    private void SetImportMode(bool active, Action updateButtonState)
    {
        _importColumnVisible = active;
        _grid.Columns["Import"]!.Visible = active;
        _importButton.Text = active ? "Apply Import..." : "Import Desktop Settings...";
        if (!active)
            _importButton.Enabled = true;
        updateButtonState();
    }
}