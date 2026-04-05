namespace RunFence.PinHelper;

public static class QuickAccessHelper
{
    public static void PinFolders(IReadOnlyList<string> paths)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        foreach (var path in paths)
            shell.Namespace(path)?.Self?.InvokeVerb("pintohome");
    }

    public static void UnpinFolders(IReadOnlyList<string> paths)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        dynamic? qa = shell.Namespace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}");
        if (qa == null) return;
        int count = qa.Items().Count;
        for (int i = 0; i < count; i++)
        {
            dynamic item = qa.Items().Item(i);
            if (paths.Contains((string)item.Path, StringComparer.OrdinalIgnoreCase))
                item.InvokeVerb("unpinfromhome");
        }
    }
}
