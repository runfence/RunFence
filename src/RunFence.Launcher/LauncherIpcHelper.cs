using System.Diagnostics;
using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.Launcher;

/// <summary>
/// Shared helper that ensures the RunFence GUI is running, waits for the IPC pipe to be ready,
/// sends a message, and returns the response. Shows an error dialog on failure.
/// Eliminates the triplicated GUI-check + start + wait + send flow.
/// </summary>
public static class LauncherIpcHelper
{
    /// <summary>
    /// Ensures the RunFence GUI is running, waits for IPC readiness, sends
    /// <paramref name="message"/>, and returns the response.
    /// Returns null if communication failed; an error dialog has already been shown in that case.
    /// </summary>
    public static IpcResponse? SendWithAutoStart(IpcMessage message)
    {
        bool guiRunning = IsGuiRunning(Constants.MutexName);

        if (guiRunning)
        {
            if (!IpcClient.PingServer())
            {
                ShowError("RunFence is not responding. Please try again or start it manually.");
                return null;
            }
        }
        else
        {
            if (!StartGui())
            {
                ShowError("Failed to start RunFence.");
                return null;
            }

            if (!WaitForServer())
            {
                ShowError("RunFence is not responding. Please try again or start it manually.");
                return null;
            }
        }

        var response = IpcClient.SendMessage(message, Constants.LauncherTimeoutMs);
        if (response == null)
        {
            ShowError("Failed to communicate with RunFence.");
            return null;
        }

        return response;
    }

    private static bool IsGuiRunning(string mutexName)
    {
        try
        {
            using var mutex = Mutex.OpenExisting(mutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Mutex exists but we can't access it (expected for non-admin accessing admin mutex)
            return true;
        }
    }

    private static bool StartGui()
    {
        try
        {
            var guiPath = Path.Combine(AppContext.BaseDirectory, "RunFence.exe");
            if (!File.Exists(guiPath))
            {
                guiPath = Path.Combine(
                    Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? "",
                    "RunFence", "RunFence.exe");
            }

            if (!File.Exists(guiPath))
                return false;

            Process.Start(new ProcessStartInfo
            {
                FileName = guiPath,
                Arguments = "--background",
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForServer()
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < Constants.LauncherTimeoutMs)
        {
            if (IpcClient.PingServer())
                return true;
            Thread.Sleep(500);
        }

        return false;
    }

    internal static void ShowError(string message)
    {
        MessageBox.Show(message, "RunFence Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}