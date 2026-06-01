using System.Text.Json;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Tokens;

namespace RunFence.Launch;

public sealed class WindowsAppsActivationLauncher(
    IWindowsAppsActivationTargetFactory targetFactory,
    IWindowsAppsActivationHelperLauncher helperLauncher,
    IWindowsAppsActivationResultPoller resultPoller,
    IMandatoryLabelService mandatoryLabelService) : IWindowsAppsActivationLauncher
{
    private const int ResultTimeoutMs = 3_000;
    private const int PollIntervalMs = 25;

    public ProcessInfo? TryLaunch(
        ProcessLaunchTarget target,
        string packageIdentitySourcePath,
        AccountLaunchIdentity originalIdentity,
        AccountLaunchIdentity resolvedIdentity)
    {
        var activationTarget = targetFactory.TryCreate(target, packageIdentitySourcePath, resolvedIdentity.Sid);
        if (activationTarget == null)
        {
            throw new InvalidOperationException(
                $"WindowsApps target '{target.ExePath}' could not be resolved for AppX activation.");
        }

        TryDeleteResultFile(activationTarget.Value.ResultFilePath);
        if (resolvedIdentity.PrivilegeLevel == PrivilegeLevel.LowIntegrity)
        {
            Directory.CreateDirectory(activationTarget.Value.ResultDirectoryPath);
            mandatoryLabelService.ApplyLowIntegrityLabel(activationTarget.Value.ResultDirectoryPath);
        }

        var helperProcess = helperLauncher.Launch(activationTarget.Value, originalIdentity, resolvedIdentity);
        if (helperProcess == null)
        {
            throw new InvalidOperationException(
                $"AppX activation launch did not return a process for '{activationTarget.Value.HelperTarget.ExePath}'.");
        }

        try
        {
            var result = WaitForResult(activationTarget.Value, helperProcess);
            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    $"AppX activation failed for '{target.ExePath}' " +
                    $"(stage={result.Stage}, exitCode={result.ExitCode}, hresult={result.HResult ?? "n/a"}): {result.Message ?? "unknown error"}");
            }

            TryDeleteResultFile(activationTarget.Value.ResultFilePath);
            try
            {
                if (helperProcess.HasExited && Directory.Exists(activationTarget.Value.ResultDirectoryPath))
                    Directory.Delete(activationTarget.Value.ResultDirectoryPath);
            }
            catch
            {
            }

            helperProcess.Dispose();

            return null;
        }
        catch
        {
            helperProcess.Dispose();
            throw;
        }
    }

    private AppxLaunchResultPayload WaitForResult(
        WindowsAppsActivationTarget activationTarget,
        IWindowsAppsActivationHelperProcess helperProcess)
    {
        var deadline = resultPoller.UtcNow.AddMilliseconds(ResultTimeoutMs);
        while (resultPoller.UtcNow < deadline)
        {
            if (TryReadResultFile(activationTarget.ResultFilePath, out var result))
                return result!;

            if (helperProcess.HasExited)
            {
                var helperExitCode = helperProcess.ExitCode;
                if (helperExitCode == 0)
                {
                    return new AppxLaunchResultPayload(
                        true,
                        "HelperExited",
                        0,
                        null,
                        null,
                        activationTarget.AppxExecutablePath,
                        activationTarget.Arguments);
                }

                if (TryReadResultFile(activationTarget.ResultFilePath, out var exitedResult))
                    return exitedResult!;

                var helperExitDetail = Enum.IsDefined(typeof(AppxLaunchExitCode), helperExitCode)
                    ? $" ({(AppxLaunchExitCode)helperExitCode})"
                    : string.Empty;
                throw new InvalidOperationException(
                    $"AppX activation helper exited without writing a usable result for '{activationTarget.AppxExecutablePath}' " +
                    $"using arguments '{activationTarget.Arguments}' (helperExitCode={helperExitCode}{helperExitDetail}).");
            }

            resultPoller.Sleep(TimeSpan.FromMilliseconds(PollIntervalMs));
        }

        if (TryReadResultFile(activationTarget.ResultFilePath, out var timedOutResult))
            return timedOutResult!;

        if (!helperProcess.HasExited)
            throw new InvalidOperationException(
                $"AppX activation helper did not finish for '{activationTarget.AppxExecutablePath}' " +
                $"using arguments '{activationTarget.Arguments}' after waiting {ResultTimeoutMs} ms.");

        var timedOutHelperExitCode = helperProcess.ExitCode;
        if (timedOutHelperExitCode == 0)
        {
            return new AppxLaunchResultPayload(
                true,
                "HelperExited",
                0,
                null,
                null,
                activationTarget.AppxExecutablePath,
                activationTarget.Arguments);
        }

        var timedOutHelperExitDetail = Enum.IsDefined(typeof(AppxLaunchExitCode), timedOutHelperExitCode)
            ? $" ({(AppxLaunchExitCode)timedOutHelperExitCode})"
            : string.Empty;
        throw new InvalidOperationException(
            $"AppX activation helper exited without writing a usable result for '{activationTarget.AppxExecutablePath}' " +
            $"using arguments '{activationTarget.Arguments}' after waiting {ResultTimeoutMs} ms (helperExitCode={timedOutHelperExitCode}{timedOutHelperExitDetail}).");
    }

    private bool TryReadResultFile(string resultFilePath, out AppxLaunchResultPayload? result)
    {
        result = null;
        if (!File.Exists(resultFilePath))
            return false;

        try
        {
            var json = File.ReadAllText(resultFilePath);
            result = JsonSerializer.Deserialize<AppxLaunchResultPayload>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result != null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"AppX activation result file '{resultFilePath}' is invalid JSON: {ex.Message}",
                ex);
        }
    }

    private static void TryDeleteResultFile(string resultFilePath)
    {
        try
        {
            if (File.Exists(resultFilePath))
                File.Delete(resultFilePath);
        }
        catch
        {
        }
    }

}
