using System.Diagnostics;
using System.Drawing.Drawing2D;
using RunFence.Core;

namespace RunFence.FolderBrowser;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string rawPath;
        if (args.Length > 0)
        {
            rawPath = string.Join(" ", args);
            if (rawPath is ['"', _, ..] && rawPath[^1] == '"')
                rawPath = rawPath[1..^1];
        }
        else
        {
            rawPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

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
                    using var dlg = new OpenFileDialog();
                    dlg.Title = Environment.UserName;
                    dlg.InitialDirectory = rawPath;
                    dlg.Filter = "All files (*.*)|*.*";
                    dlg.CheckFileExists = true; // We don't launch multiple files but the purpose of this is not to launch,
                    // it's for browsing files instead of running explorer.exe.
                    // So the multiselect gives copy/cut capability for multiple files.
                    dlg.Multiselect = true;
                    dlg.ShowPreview = true;
                    dlg.CheckPathExists = true;
                    dlg.ShowHiddenFiles = true;
                    dlg.ShowPinnedPlaces = true;
                    dlg.DereferenceLinks = true;
                    dlg.SupportMultiDottedExtensions = true;
                    dlg.CustomPlaces.Add(new FileDialogCustomPlace(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)));
                    dlg.CustomPlaces.Add(new FileDialogCustomPlace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
                    dlg.CustomPlaces.Add(new FileDialogCustomPlace(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)));
                    dlg.CustomPlaces.Add(new FileDialogCustomPlace(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\"));

                    if (dlg.ShowDialog(ownerForm) == DialogResult.OK)
                    {
                        if (dlg.FileNames.Length > 1)
                        {
                            MessageBox.Show("Launching multiple files is not supported.",
                                "Folder Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            LaunchFile(dlg.FileName);
                        }
                    }
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
        catch
        {
            // Best effort — silently ignore launch failures
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