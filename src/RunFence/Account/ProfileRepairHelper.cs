using System.ComponentModel;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Account;

public class ProfileRepairHelper(
    IProfileRepairPrompt prompt,
    IProfileCorruptionDetector profileCorruptionDetector,
    IProfileRegistryRepairer profileRegistryRepairer,
    IProcessTerminationService processTerminationService,
    IRunFenceRestartService runFenceRestartService,
    ILoggingService log,
    NTTranslateApi ntTranslate) : IProfileRepairHelper
{
    /// <summary>
    /// Wraps a launch action with automatic profile corruption detection and repair
    /// for the specified account SID.
    /// On launch failure: checks if the profile for <paramref name="accountSid"/> was corrupted,
    /// prompts the user to repair, and optionally restarts RunFence after a successful repair.
    /// If no corruption is detected or <paramref name="accountSid"/> is null,
    /// the original exception is rethrown for normal handling.
    /// </summary>
    public T ExecuteWithProfileRepair<T>(Func<T> launchAction, string? accountSid)
    {
        try
        {
            return launchAction();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            throw;
        }
        catch
        {
            if (accountSid == null)
                throw;

            var corrupted = profileCorruptionDetector.Detect(accountSid);
            if (corrupted == null)
                throw;

            var accountName = TryResolveAccountName(corrupted.Sid) ?? corrupted.Sid;
            log.Warn($"Profile corruption detected for '{accountName}' (SID={corrupted.Sid}): " +
                     $"original='{corrupted.OriginalPath}', temp='{corrupted.TempPath}'");

            if (!prompt.ConfirmRepair(accountName))
            {
                log.Warn($"User declined profile repair for: {accountName}");
                throw;
            }

            if (!KillAccountProcesses(corrupted.Sid, accountName))
            {
                prompt.NotifyRepairFailed();
                throw;
            }

            if (!profileRegistryRepairer.Repair(corrupted))
            {
                prompt.NotifyRepairFailed();
                throw;
            }

            if (prompt.ConfirmRestartRunFence())
                runFenceRestartService.Restart();

            throw new OperationCanceledException();
        }
    }

    private bool KillAccountProcesses(string sid, string accountName)
    {
        try
        {
            var result = processTerminationService.KillProcesses(sid);
            if (result.Failed == 0)
            {
                log.Info($"ProfileRepair: killed {result.Killed} process(es) for '{accountName}' before repair.");
                return true;
            }

            log.Error(
                $"ProfileRepair: failed to kill all processes for '{accountName}' before repair. " +
                $"Killed={result.Killed}, failed={result.Failed}");
            return false;
        }
        catch (Exception ex)
        {
            log.Error($"ProfileRepair: failed to enumerate or kill processes for '{accountName}': {ex.Message}");
            return false;
        }
    }

    private string? TryResolveAccountName(string sid)
    {
        try
        {
            return ntTranslate.TranslateName(sid).Value;
        }
        catch
        {
            return null;
        }
    }
}
