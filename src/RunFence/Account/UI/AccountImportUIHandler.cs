using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.PrefTrans.UI.Forms;
using RunFence.UI.Forms;

namespace RunFence.Account.UI;

public class AccountImportUIHandler(
    IAccountImportHandler importHandler,
    IOpenFileDialogAdapterFactory openFileDialogFactory)
{
    private DataGridView _grid = null!;
    private ToolStripButton _importButton = null!;
    private bool _importColumnVisible;

    public void Initialize(DataGridView grid, ToolStripButton importButton)
    {
        _grid = grid;
        _importButton = importButton;
    }

    public async Task HandleImportClickAsync(IAccountsPanelContext context)
    {
        if (!_importColumnVisible)
        {
            SetImportMode(true, context.UpdateButtonState);

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
            SetImportMode(false, context.UpdateButtonState);
            return;
        }

        await ImportToAccountsAsync(selectedAccounts, context);

        SetImportMode(false, context.UpdateButtonState);
        foreach (DataGridViewRow row in _grid.Rows)
            row.Cells["Import"].Value = false;
    }

    private async Task ImportToAccountsAsync(List<AccountRow> accounts, IAccountsPanelContext context)
    {
        var owner = context.OwnerControl;
        context.SetControlsEnabled(false);
        context.OperationGuard.Begin(owner);

        try
        {
            var importAccounts = accounts
                .Select(a => new ImportAccount(
                    a.Credential ?? new CredentialEntry { Sid = a.Sid }, a.Username))
                .ToList();

            string selectedFile;
            using (var dlgAdapter = openFileDialogFactory.Create())
            {
                var dlg = dlgAdapter.Dialog;
                DesktopSettingsImportDialog.Setup(dlg, context.Database.LastPrefsFilePath);
                if (dlgAdapter.ShowDialog(owner.FindForm()) != DialogResult.OK)
                    return;
                selectedFile = dlg.FileName;
            }

            var parent = owner.FindForm();
            var logForm = new ImportProgressForm(parent); // intentionally no using — form closes/disposes itself when user clicks OK
            if (parent != null)
                logForm.Show(parent);
            else
                logForm.Show();

            var sink = new ImportProgressSink(selectedFile,
                logForm.AppendLog,
                logForm.EnableOkButton,
                text => { if (!owner.IsDisposed) context.UpdateStatus(text); });

            var settingsPath = await importHandler.RunImportAsync(importAccounts, context.CredentialStore, sink);

            if (settingsPath != null && !owner.IsDisposed)
                context.SaveLastPrefsPath(settingsPath);
        }
        finally
        {
            context.OperationGuard.End(owner);
            if (!owner.IsDisposed)
            {
                context.SetControlsEnabled(true);
                context.UpdateButtonState();
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
