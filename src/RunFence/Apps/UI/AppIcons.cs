namespace RunFence.Apps.UI;

public static class AppIcons
{
    private static Icon? _appIcon;

    public static Icon GetAppIcon()
    {
        if (_appIcon != null)
            return _appIcon;

        var assembly = typeof(AppIcons).Assembly;
        using var stream = assembly.GetManifestResourceStream("RunFence.app.ico")!;
        return _appIcon = new Icon(stream);
    }
}