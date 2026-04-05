using System.IO.Pipes;

namespace RunFence.DragBridge;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var parsed = ParseArgs(args);
        if (parsed == null)
        {
            MessageBox.Show("Invalid arguments.", "DragBridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Connect to the parent's pipe before creating any windows. The parent sends
        // a ready signal after verifying our identity, and the window uses
        // AttachThreadInput + SetForegroundWindow to appear above other windows.
        var pipe = new NamedPipeClientStream(".", parsed.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(10_000);
        }
        catch
        {
            pipe.Dispose();
            return;
        }

        try
        {
            if (pipe.ReadByte() < 0)
            {
                pipe.Dispose();
                return;
            }
        }
        catch
        {
            pipe.Dispose();
            return;
        }

        Application.Run(new DragBridgeWindow(pipe, parsed.X, parsed.Y, parsed.RunFencePid, parsed.RestoreHwnd));
    }

    private record ParsedArgs(string PipeName, int X, int Y, int RunFencePid = 0, nint RestoreHwnd = 0);

    private static ParsedArgs? ParseArgs(string[] args)
    {
        string? pipe = null;
        int x = 0, y = 0, runFencePid = 0;
        nint restoreHwnd = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe" when i + 1 < args.Length:
                    pipe = args[++i];
                    break;
                case "--x" when i + 1 < args.Length:
                    int.TryParse(args[++i], out x);
                    break;
                case "--y" when i + 1 < args.Length:
                    int.TryParse(args[++i], out y);
                    break;
                case "--runfence-pid" when i + 1 < args.Length:
                    int.TryParse(args[++i], out runFencePid);
                    break;
                case "--restore-hwnd" when i + 1 < args.Length:
                    nint.TryParse(args[++i], out restoreHwnd);
                    break;
            }
        }

        if (pipe == null)
            return null;
        return new ParsedArgs(pipe, x, y, runFencePid, restoreHwnd);
    }
}