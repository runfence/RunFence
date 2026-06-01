using System.Runtime.InteropServices;

namespace RunFence.PinHelper;

public static class QuickAccessHelper
{
    public static void PinFolders(IReadOnlyList<string> paths)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try
        {
            foreach (var path in paths)
            {
                try
                {
                    shell.Namespace(path)?.Self?.InvokeVerb("pintohome");
                }
                catch (COMException)
                {
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    public static void UnpinFolders(IReadOnlyList<string> paths)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try
        {
            dynamic? qa = shell.Namespace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}");
            if (qa == null)
                return;

            var items = qa.Items();
            int count = items.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                try
                {
                    dynamic item = items.Item(i);
                    if (paths.Contains((string)item.Path, StringComparer.OrdinalIgnoreCase))
                        item.InvokeVerb("unpinfromhome");
                }
                catch (COMException)
                {
                }
            }
        }
        catch (COMException)
        {
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }
}
