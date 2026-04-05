using RunFence.Apps.Shortcuts;
using RunFence.Core;

namespace RunFence.TrayIcon;

public static class TrayMenuDiscoveryBuilder
{
    public static List<ToolStripItem> BuildMenuItems(
        List<StartMenuEntry> entries,
        IReadOnlyDictionary<string, string> sidNames,
        Dictionary<string, Image?> iconCache,
        Action<string, string> onLaunch,
        SidDisplayNameResolver displayNameResolver,
        ILoggingService? log = null)
    {
        var topLevelItems = new List<ToolStripItem>();

        var accountGroups = entries
            .GroupBy(e => e.AccountSid, StringComparer.OrdinalIgnoreCase);

        foreach (var accountGroup in accountGroups)
        {
            var sid = accountGroup.Key;
            var accountEntries = accountGroup.ToList();

            if (accountEntries.Count == 1)
            {
                var entry = accountEntries[0];
                var label = $"{entry.Name} as {GetDisplayUsername(sid, sidNames, displayNameResolver)}";
                topLevelItems.Add(CreateMenuItem(label, entry, iconCache, onLaunch, log));
            }
            else
            {
                var username = GetDisplayUsername(sid, sidNames, displayNameResolver);
                var accountMenu = new ToolStripMenuItem($"{username} Apps");

                var subfolderGroups = accountEntries
                    .Where(e => e.Subfolder != null)
                    .GroupBy(e => e.Subfolder!, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() >= 2)
                    .ToDictionary(g => g.Key, StringComparer.OrdinalIgnoreCase);

                var usedInSubfolder = new HashSet<StartMenuEntry>();

                foreach (var (subfolder, group) in subfolderGroups.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var subfolderMenu = new ToolStripMenuItem(subfolder);
                    foreach (var e in group.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        subfolderMenu.DropDownItems.Add(CreateMenuItem(e.Name, e, iconCache, onLaunch, log));
                        usedInSubfolder.Add(e);
                    }

                    accountMenu.DropDownItems.Add(subfolderMenu);
                }

                foreach (var e in accountEntries
                             .Where(e => !usedInSubfolder.Contains(e))
                             .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                {
                    accountMenu.DropDownItems.Add(CreateMenuItem(e.Name, e, iconCache, onLaunch, log));
                }

                topLevelItems.Add(accountMenu);
            }
        }

        topLevelItems.Sort((a, b) =>
            StringComparer.OrdinalIgnoreCase.Compare(a.Text, b.Text));

        return topLevelItems;
    }

    private static ToolStripMenuItem CreateMenuItem(string label, StartMenuEntry entry,
        Dictionary<string, Image?> iconCache, Action<string, string> onLaunch, ILoggingService? log)
    {
        var item = new ToolStripMenuItem(label);
        item.Image = GetOrLoadIcon(entry.ExePath, iconCache, log);
        var capturedExe = entry.ExePath;
        var capturedSid = entry.AccountSid;
        item.Click += (_, _) => onLaunch(capturedExe, capturedSid);
        return item;
    }

    private static string GetDisplayUsername(string sid, IReadOnlyDictionary<string, string> sidNames,
        SidDisplayNameResolver resolver)
    {
        var display = resolver.GetDisplayName(sid, null, sidNames);
        var slash = display.LastIndexOf('\\');
        return slash >= 0 ? display[(slash + 1)..] : display;
    }

    private static Image? GetOrLoadIcon(string exePath, Dictionary<string, Image?> iconCache, ILoggingService? log)
    {
        if (iconCache.TryGetValue(exePath, out var cached))
            return (Image?)cached?.Clone();

        var icon = ShortcutIconHelper.ExtractIcon(exePath, log: log);
        iconCache[exePath] = icon;
        return (Image?)icon?.Clone();
    }
}