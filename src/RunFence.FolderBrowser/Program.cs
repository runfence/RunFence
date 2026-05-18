using System.Diagnostics;
using System.Drawing.Drawing2D;
using RunFence.Core;
using RunFence.Core.Infrastructure;

namespace RunFence.FolderBrowser;

public static class Program
{
    private sealed record FolderBrowserCommand(string RawPathTail);

    public interface IOpenFileDialogAdapter : IDisposable
    {
        OpenFileDialog Dialog { get; }
        void AddInteractiveUserCustomPlaces();
        DialogResult ShowDialog(IWin32Window? owner);
    }

    private sealed class OpenFileDialogAdapter : IOpenFileDialogAdapter
    {
        private readonly OpenFileDialog _dialog = new();
        public OpenFileDialog Dialog => _dialog;

        public void AddInteractiveUserCustomPlaces() => FileDialogHelper.AddInteractiveUserCustomPlaces(_dialog);
        public DialogResult ShowDialog(IWin32Window? owner) => _dialog.ShowDialog(owner);
        public void Dispose() => _dialog.Dispose();
    }

    [STAThread]
    static void Main(string[] args)
    {
        var command = Route(args);
        var rawPath = command.RawPathTail;

        if (!Directory.Exists(rawPath))
            rawPath = "";

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Owner form for taskbar presence.
        // FormBorderStyle.None avoids WS_EX_TOOLWINDOW which can intermittently
        // prevent the form from holding the taskbar slot, causing the native dialog
        // to fall back to the exe's embedded (default) icon.
        using var ownerForm = new Form();
        ownerForm.ShowInTaskbar = true;
        ownerForm.Opacity = 0;
        ownerForm.Size = new Size(0, 0);
        ownerForm.StartPosition = FormStartPosition.CenterScreen;
        ownerForm.Text = Environment.UserName;
        ownerForm.Icon = CreateAppIcon();
        ownerForm.FormBorderStyle = FormBorderStyle.None;

        ownerForm.Shown += (_, _) =>
        {
            // Defer to the message pump so the form is fully activated before showing the
            // dialog. When launched via CreateProcessWithTokenW (e.g. AppContainer), the
            // process may not have foreground activation yet during the Shown event —
            // IFileDialog::Show can return ERROR_CANCELLED immediately in that case.
            ownerForm.BeginInvoke(() =>
            {
                if (ownerForm.WindowState == FormWindowState.Minimized)
                    ownerForm.WindowState = FormWindowState.Normal;
                ownerForm.Activate();

                try
                {
                    using var dlgAdapter = new OpenFileDialogAdapter();
                    ShowDialogAndLaunch(ownerForm, rawPath, dlgAdapter);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, Environment.UserName,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    ownerForm.Close();
                }
            });
        };

        Application.Run(ownerForm);
    }

    public static void ShowDialogAndLaunch(Form ownerForm, string rawPath, IOpenFileDialogAdapter dlgAdapter)
    {
        var dlg = dlgAdapter.Dialog;
        dlg.Title = Environment.UserName;
        dlg.InitialDirectory = rawPath;
        dlg.Filter = "All files (*.*)|*.*";
        dlg.CheckFileExists = true;
        dlg.Multiselect = true;
        dlg.ShowPreview = true;
        dlg.CheckPathExists = true;
        dlg.ShowHiddenFiles = true;
        dlg.ShowPinnedPlaces = true;
        dlg.DereferenceLinks = true;
        dlg.SupportMultiDottedExtensions = true;
        dlgAdapter.AddInteractiveUserCustomPlaces();
        if (dlgAdapter.ShowDialog(ownerForm) != DialogResult.OK)
            return;

        if (dlg.FileNames.Length > 1)
        {
            MessageBox.Show("Launching multiple files is not supported.",
                "Folder Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        LaunchFile(dlg.FileName);
    }

    private static FolderBrowserCommand Route(string[] args)
    {
        if (args.Length < 1)
            return new FolderBrowserCommand(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        var tail = CommandLineHelper.SkipArgs(Environment.CommandLine, 1) ?? string.Empty;
        var rawPath = tail;
        if (rawPath is ['"', _, ..] && rawPath[^1] == '"')
            rawPath = rawPath[1..^1];
        return new FolderBrowserCommand(rawPath);
    }

    private static void LaunchFile(string filePath)
    {
        try
        {
            // SmartScreen dialog does not work with the folder browser app (launched as a different
            // user account without interactive SmartScreen consent), so strip Zone.Identifier instead.
            var zoneFile = filePath + ":Zone.Identifier";
            var hasZoneIdentifier = false;
            try
            {
                using var _ = File.Open(zoneFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                hasZoneIdentifier = true;
            }
            catch
            {
            }

            if (hasZoneIdentifier)
            {
                var confirm = MessageBox.Show(
                    $"This file was downloaded from the internet:\n\n{Path.GetFileName(filePath)}\n\n" +
                    $"Do you want to unblock it and launch it anyway?",
                    "Security Warning",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirm != DialogResult.Yes)
                    return;
                try
                {
                    File.Delete(zoneFile);
                }
                catch
                {
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(filePath) ?? ""
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file:\n{Path.GetFileName(filePath)}\n\n{ex.Message}",
                Environment.UserName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Icon CreateAppIcon()
        => IconBuilder.CreateMultiSizeIcon(
            [16, 32, 48, 256],
            (g, size) => DrawFolderIcon(g, size));

    private static void DrawFolderIcon(Graphics g, int size)
    {
        float pad = size * 0.06f;
        float d = size - 2 * pad;

        using var bgBrush = new SolidBrush(Color.FromArgb(0x33, 0x66, 0xCC));
        g.FillEllipse(bgBrush, pad, pad, d, d);

        float borderW = Math.Max(1f, size / 24f);
        using var borderPen = new Pen(Color.White, borderW);
        g.DrawEllipse(borderPen, pad + borderW / 2, pad + borderW / 2, d - borderW, d - borderW);

        using var clipPath = new GraphicsPath();
        clipPath.AddEllipse(pad, pad, d, d);
        g.SetClip(clipPath);

        using var whiteBrush = new SolidBrush(Color.White);

        float fw = d * 0.55f;
        float fh = d * 0.42f;
        float fx = pad + (d - fw) / 2;
        float fy = pad + d * 0.33f;

        // Folder tab (top-left bump)
        float tabW = fw * 0.38f;
        float tabH = fh * 0.22f;
        g.FillRectangle(whiteBrush, fx, fy, tabW, tabH);

        // Folder body
        g.FillRectangle(whiteBrush, fx, fy + tabH, fw, fh);

        g.ResetClip();
    }
}
