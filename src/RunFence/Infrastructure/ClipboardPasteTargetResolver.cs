using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class ClipboardPasteTargetResolver(
    IForegroundWindowResolver foregroundWindowResolver,
    IWindowProcessIdReader windowProcessIdReader,
    IConsoleHostProcessResolver consoleHostProcessResolver,
    IClipboardFormatReader clipboardFormatReader,
    IProcessJobManager jobManager,
    ILoggingService log) : IClipboardPasteTargetResolver
{
    public ClipboardPasteTargetResolution Resolve()
    {
        var foreground = foregroundWindowResolver.GetForegroundWindow();
        int foregroundProcessId = (int)foreground.ProcessId;
        if (foregroundProcessId == 0)
            return ClipboardPasteTargetResolution.Passthrough();

        if (jobManager.TryGetRestrictedJobForPid(foregroundProcessId) == IntPtr.Zero)
            return ClipboardPasteTargetResolution.Passthrough();

        int targetProcessId = foregroundProcessId;
        if (foreground.ClassName == WindowNative.ConsoleWindowClass)
        {
            if (!consoleHostProcessResolver.TryGetConsoleHostProcessId(foregroundProcessId, out int consoleHostProcessId))
            {
                string message = $"ClipboardPasteInterceptService: Could not resolve conhost pid for cmd pid {foregroundProcessId}, passing through.";
                log.Warn(message);
                return ClipboardPasteTargetResolution.Passthrough(message);
            }

            targetProcessId = consoleHostProcessId;
        }

        IntPtr ownerHwnd = clipboardFormatReader.GetClipboardOwnerWindow();
        uint ownerProcessId = windowProcessIdReader.GetWindowProcessId(ownerHwnd);
        if (ownerProcessId == (uint)targetProcessId)
        {
            log.Debug($"ClipboardPasteInterceptService: Paste on pid {targetProcessId} - clipboard owner is same process, native paste.");
            return ClipboardPasteTargetResolution.Passthrough();
        }

        return ClipboardPasteTargetResolution.Intercept(new ClipboardPasteTarget(
            foreground.HWnd,
            foregroundProcessId,
            targetProcessId,
            ownerProcessId));
    }
}
