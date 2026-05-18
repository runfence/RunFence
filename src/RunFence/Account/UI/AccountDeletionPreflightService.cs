using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

public class AccountDeletionPreflightService(
    IAccountLifecycleManager lifecycleManager,
    IProcessTerminationService processTerminationService,
    IAccountMessageBoxService messageBoxService)
{
    private static readonly HashSet<string> HelperProcessPaths = BuildHelperProcessPaths();

    public async Task<bool> EnsureNoBlockingProcessesAsync(AccountDeletionPreflightRequest request)
    {
        var deleteValidation = await lifecycleManager.ValidateDeleteAsync(request.Sid);
        if (!deleteValidation.HasRunningProcesses)
            return true;

        if (request.IsUnavailable || request.IsSystemSid)
        {
            messageBoxService.Show(
                null,
                BuildRunningProcessesError(deleteValidation.RunningProcesses),
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        var hasOnlyRunFenceHelperProcesses = deleteValidation.RunningProcesses.All(process =>
        {
            if (string.IsNullOrWhiteSpace(process.ExecutablePath))
                return false;

            try
            {
                return HelperProcessPaths.Contains(Path.GetFullPath(process.ExecutablePath));
            }
            catch
            {
                return false;
            }
        });

        if (!hasOnlyRunFenceHelperProcesses)
        {
            var killPrompt = $"The following processes are still running under \"{request.DisplayName}\".\n\n" +
                             "They will all be killed before the account is deleted.\n\n" +
                             $"{string.Join(Environment.NewLine, deleteValidation.RunningProcesses.Select(FormatProcessListEntry))}\n\n" +
                             "Press OK to kill them and continue, or Cancel to keep the account.";

            if (messageBoxService.Show(
                    null,
                    killPrompt,
                    "Delete Account",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
                return false;
        }

        ProcessKillResult killResult;
        try
        {
            killResult = await Task.Run(() => processTerminationService.KillProcesses(request.Sid));
        }
        catch (Exception ex)
        {
            messageBoxService.Show(
                null,
                $"Failed to enumerate or kill processes: {ex.Message}",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        deleteValidation = await lifecycleManager.ValidateDeleteAsync(request.Sid);
        if (deleteValidation.ErrorMessage != null)
        {
            messageBoxService.Show(
                null,
                deleteValidation.ErrorMessage,
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        if (!deleteValidation.HasRunningProcesses)
            return true;

        var message = killResult.Failed > 0
            ? "Some processes could not be terminated, so the account was not deleted.\n\n"
            : "The account still has running processes, so it was not deleted.\n\n";
        messageBoxService.Show(
            null,
            message + BuildRunningProcessesError(deleteValidation.RunningProcesses),
            "Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        return false;
    }

    private static string BuildRunningProcessesError(IReadOnlyList<ProcessInfo> runningProcesses)
        => "Cannot delete this account while it has running processes:\n" +
           string.Join("\n", runningProcesses.Select(FormatProcessListEntry));

    private static HashSet<string> BuildHelperProcessPaths()
    {
        var helperPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PathConstants.JobKeeperExeName)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PathConstants.ProfileKeeperExeName)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PathConstants.DragBridgeExeName)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PathConstants.PinHelperExeName)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName)),
            Path.GetFullPath(Path.Combine(Environment.SystemDirectory, "conhost.exe"))
        };

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDir))
            helperPaths.Add(Path.GetFullPath(Path.Combine(windowsDir, "SysWOW64", "conhost.exe")));

        return helperPaths;
    }

    private static string FormatProcessListEntry(ProcessInfo process)
        => $"- {(process.ExecutablePath != null
            ? Path.GetFileName(process.ExecutablePath)
            : "Unknown process")} (PID {process.Pid})";
}
