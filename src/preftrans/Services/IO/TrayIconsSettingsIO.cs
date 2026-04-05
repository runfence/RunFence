using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public static class TrayIconsSettingsIO
{
    public static TrayIconsSettings Read()
    {
        var trayIcons = new TrayIconsSettings();
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegExplorer);
            if (key?.GetValue("EnableAutoTray") is int v)
                trayIcons.EnableAutoTray = v;
        }, "reading");
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegNotifyIconSettings);
            if (key == null)
                return;
            trayIcons.PerAppVisibility = new List<TrayIconEntry>();
            foreach (var subName in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(subName);
                if (sub == null)
                    continue;
                var entry = new TrayIconEntry
                {
                    ExecutablePath = sub.GetValue("ExecutablePath") as string,
                    IsPromoted = sub.GetValue("IsPromoted") as int?,
                };
                if (entry.ExecutablePath != null)
                    trayIcons.PerAppVisibility.Add(entry);
            }
        }, "reading");
        return trayIcons;
    }

    public static void Write(TrayIconsSettings trayIcons)
    {
        bool changed = false;
        SafeExecutor.Try(() =>
        {
            if (trayIcons.EnableAutoTray.HasValue)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegExplorer);
                key.SetValue("EnableAutoTray", trayIcons.EnableAutoTray.Value, RegistryValueKind.DWord);
                changed = true;
            }
        }, "writing");
        SafeExecutor.Try(() =>
        {
            if (trayIcons.PerAppVisibility != null)
            {
                using var root = Registry.CurrentUser.OpenSubKey(Constants.RegNotifyIconSettings, writable: true);
                if (root == null)
                    return;
                var existingByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var subName in root.GetSubKeyNames())
                {
                    using var sub = root.OpenSubKey(subName);
                    if (sub?.GetValue("ExecutablePath") is string ep)
                        existingByPath[ep] = subName;
                }

                foreach (var entry in trayIcons.PerAppVisibility)
                {
                    if (entry.ExecutablePath == null || !entry.IsPromoted.HasValue)
                        continue;
                    if (existingByPath.TryGetValue(entry.ExecutablePath, out var keyName))
                    {
                        using var sub = root.OpenSubKey(keyName, writable: true);
                        sub?.SetValue("IsPromoted", entry.IsPromoted.Value, RegistryValueKind.DWord);
                    }
                    else
                    {
                        // Skip creating new bare-minimum subkeys — Explorer crashes at startup when
                        // it reads entries that are missing the fields it manages internally (icon
                        // UID, GUID, etc.). Only update entries that already exist in the registry.
                        continue;
                    }

                    changed = true;
                }
            }
        }, "writing");
        if (changed)
            BroadcastHelper.Broadcast();
    }
}