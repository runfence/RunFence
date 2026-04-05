using RunFence.Apps.UI;

namespace RunFence.Infrastructure;

public class AppIconProvider : IAppIconProvider
{
    public Icon GetAppIcon() => AppIcons.GetAppIcon();
}