namespace RunFence.PinHelper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Quick Access update failed: {ex.Message}");
            return 0;
        }
    }
}
