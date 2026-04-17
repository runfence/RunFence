using System.Diagnostics;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Core.Models;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Wizard template that replaces broad-access ACEs (Users, Authenticated Users, Everyone) on
/// fixed drives (excluding the system drive) with ACEs for a chosen target SID.
/// Before modifying each drive, backs up its current ACLs via <c>icacls /save</c> to
/// <c>%LOCALAPPDATA%\RunFence\drive-acl-backup.txt</c> (append-only with timestamp header per run).
/// </summary>
public class PrepareSystemTemplate(
    DriveAclReplacer driveAclReplacer,
    IWizardSessionSaver sessionSaver,
    SessionContext session,
    IQuickAccessPinService quickAccessPinService)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "Prepare System";
    public string Description => "Replace broad ACEs on data drives so only you (or Administrators) can read files.";
    public string IconEmoji => "\U0001F6E1\uFE0F";
    public Action<IWin32Window>? PostWizardAction => null;

    public void Cleanup()
    {
    }

    public bool IsPrerequisite => true;

    /// <summary>
    /// True when at least one non-system fixed drive has replaceable broad ACEs.
    /// The result is pre-computed by <see cref="WarmCacheAsync"/> to avoid disk I/O on the UI
    /// thread; <see cref="IsAvailable"/> returns the cached value after warm-up.
    /// </summary>
    public bool IsAvailable => _cachedIsAvailable ?? HasAnyApplicableDrive();

    private bool? _cachedIsAvailable;

    /// <summary>
    /// Pre-scans drives on a background thread so that <see cref="IsAvailable"/> never performs
    /// disk I/O on the UI thread. Called by the wizard launcher for all templates concurrently
    /// before the dialog is constructed.
    /// </summary>
    public Task WarmCacheAsync()
    {
        return Task.Run(() => { _cachedIsAvailable = HasAnyApplicableDrive(); });
    }

    private bool HasAnyApplicableDrive()
    {
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        return DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed)
            .Where(drive => !string.Equals(drive.RootDirectory.FullName, systemDrive, StringComparison.OrdinalIgnoreCase))
            .Any(drive => driveAclReplacer.HasReplaceableBroadAces(drive.RootDirectory.FullName));
    }

    public IReadOnlyList<WizardStepPage> CreateSteps() =>
    [
        new PrepareSystemDriveStep(
            selections => _data.DriveSelections = selections,
            session.Database.SidNames,
            driveAclReplacer)
    ];

    public async Task ExecuteAsync(IWizardProgressReporter progress)
    {
        if (_data.DriveSelections == null || _data.DriveSelections.Count == 0)
        {
            progress.ReportError("No drives were selected.");
            sessionSaver.SaveAndRefresh();
            return;
        }

        var backupPath = GetAclBackupPath();

        foreach (var (drivePath, targetSid) in _data.DriveSelections)
        {
            progress.ReportStatus($"Backing up ACLs on {drivePath}...");
            await Task.Run(() => BackupDriveAcl(drivePath, backupPath, progress));

            progress.ReportStatus($"Replacing ACLs on {drivePath}...");
            var error = await Task.Run(() => driveAclReplacer.ReplaceDriveAcl(drivePath, targetSid));
            if (error != null)
                progress.ReportError($"{drivePath}: {error}");
            else
            {
                progress.ReportStatus($"{drivePath}: done.");
                quickAccessPinService.PinFolders(targetSid, [drivePath]);
            }
        }

        sessionSaver.SaveAndRefresh();
    }

    /// <summary>
    /// Runs <c>icacls &lt;drivePath&gt; /save &lt;tempFile&gt;</c> to capture the ACL data in icacls format,
    /// then appends a timestamp header and the saved content to <paramref name="backupFilePath"/>.
    /// Non-fatal — errors are reported to <paramref name="progress"/> but never abort the ACL replacement.
    /// </summary>
    private static void BackupDriveAcl(string drivePath, string backupFilePath, IWizardProgressReporter? progress)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath)!);

            var tempFile = Path.Combine(Path.GetTempPath(), $"rfacl_{Guid.NewGuid():N}.tmp");
            try
            {
                var psi = new ProcessStartInfo("icacls.exe", $"\"{drivePath}\" /save \"{tempFile}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                    if (proc != null && !proc.WaitForExit(10_000))
                        proc.Kill();

                var header = $"=== ACL backup: {drivePath} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}";
                File.AppendAllText(backupFilePath, header);

                if (File.Exists(tempFile))
                {
                    using var fs = new FileStream(backupFilePath, FileMode.Append, FileAccess.Write);
                    using var src = File.OpenRead(tempFile);
                    src.CopyTo(fs);
                }

                File.AppendAllText(backupFilePath, Environment.NewLine);
            }
            finally
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            // Backup is best-effort — report but do not abort ACL replacement on backup failure.
            progress?.ReportError($"ACL backup for {drivePath} failed: {ex.Message}");
        }
    }

    private static string GetAclBackupPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "RunFence", "drive-acl-backup.txt");
    }

    private sealed class CommitData
    {
        public List<(string DrivePath, string TargetSid)>? DriveSelections { get; set; }
    }
}
