using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class NotificationsSettingsIO(ISafeExecutor safe, IBroadcastHelper broadcast) : ISettingsIO
{
    public NotificationSettings Read()
    {
        var notifications = new NotificationSettings();
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegNotificationSettings);
            if (key == null)
                return;
            if (key.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED") is int v)
                notifications.GlobalToastsEnabled = v;
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegNotificationSettings);
            if (key == null)
                return;
            notifications.PerAppSuppression = new Dictionary<string, bool>();
            foreach (var subName in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(subName);
                if (sub == null)
                    continue;
                var enabled = sub.GetValue("Enabled");
                if (enabled is int i)
                    notifications.PerAppSuppression[subName] = i != 0;
                else
                    notifications.PerAppSuppression[subName] = true; // missing Enabled value = allowed
            }
        }, "reading");
        return notifications;
    }

    public void Write(NotificationSettings notifications)
    {
        bool changed = false;
        safe.Try(() =>
        {
            if (notifications.GlobalToastsEnabled.HasValue)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegNotificationSettings);
                key.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", notifications.GlobalToastsEnabled.Value, RegistryValueKind.DWord);
                changed = true;
            }
        }, "writing");
        safe.Try(() =>
        {
            if (notifications.PerAppSuppression != null)
            {
                foreach (var (appId, enabled) in notifications.PerAppSuppression)
                {
                    if (appId.Contains('\\') || appId.Contains('/'))
                        continue;
                    safe.Try(() =>
                    {
                        using var key = Registry.CurrentUser.CreateSubKey(Constants.RegNotificationSettings + @"\" + appId);
                        key.SetValue("Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
                        changed = true;
                    }, "writing");
                }
            }
        }, "writing");
        if (changed)
            broadcast.Broadcast();
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.Notifications = Read();

    void ISettingsIO.WriteFrom(UserSettings s) { if (s.Notifications != null) Write(s.Notifications); }
}