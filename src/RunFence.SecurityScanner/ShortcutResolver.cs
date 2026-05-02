using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace RunFence.SecurityScanner;

public class ShortcutResolver
{
    public string? ResolveShortcutTarget(string lnkPath)
    {
        string? result = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return;
                dynamic shell = Activator.CreateInstance(shellType)!;
                try
                {
                    dynamic shortcut = shell.CreateShortcut(lnkPath);
                    try
                    {
                        result = shortcut.TargetPath as string;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(shortcut);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(10_000))
        {
            thread.Interrupt();
            return null;
        }
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }
}
