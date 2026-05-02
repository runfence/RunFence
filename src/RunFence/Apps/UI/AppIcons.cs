namespace RunFence.Apps.UI;

public static class AppIcons
{
    private static readonly Lazy<Icon> _appIcon = new(() =>
    {
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream("RunFence.app.ico")!;
        return new Icon(stream);
    });

    public static Icon GetAppIcon() => _appIcon.Value;
}
