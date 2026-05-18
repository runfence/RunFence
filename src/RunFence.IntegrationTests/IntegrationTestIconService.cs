using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.IntegrationTests;

internal sealed class IntegrationTestIconService(string iconPath) : IIconService
{
    public string CreateBadgedIcon(AppEntry app, string? customIconPath = null) => iconPath;
    public bool NeedsRegeneration(AppEntry app) => false;
    public string GetIconPath(string appId) => iconPath;
    public void DeleteIcon(string appId) { }
    public Image? GetOriginalAppIcon(AppEntry app, int size = 16) => null;
}
