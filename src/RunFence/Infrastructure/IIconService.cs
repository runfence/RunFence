using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public interface IIconService
{
    string CreateBadgedIcon(AppEntry app, string? customIconPath = null);
    bool NeedsRegeneration(AppEntry app);
    void DeleteIcon(string appId);
    Image? GetOriginalAppIcon(AppEntry app, int size = 16);
}