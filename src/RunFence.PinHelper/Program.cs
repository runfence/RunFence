using RunFence.PinHelper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        switch (args)
        {
            case ["--pin-folders", ..]:
                QuickAccessHelper.PinFolders(args.Skip(1).ToList());
                return 0;
            case ["--unpin-folders", ..]:
                QuickAccessHelper.UnpinFolders(args.Skip(1).ToList());
                return 0;
            default:
                return 1;
        }
    }
}
