using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class ExplorerSettingsIO(ISafeExecutor safe, IBroadcastHelper broadcast) : ISettingsIO
{
    public ExplorerSettings Read()
    {
        var explorer = new ExplorerSettings();
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegExplorerAdvanced);
            if (key == null)
                return;
            explorer.ShowHiddenFiles = key.GetValue("Hidden") as int?;
            explorer.ShowFileExtensions = key.GetValue("HideFileExt") as int?;
            explorer.ShowSuperHidden = key.GetValue("ShowSuperHidden") as int?;
            explorer.ShowFullPathInTitleBar = key.GetValue("FullPath") as int?;
            explorer.LaunchFolderInSeparateProcess = key.GetValue("SeparateProcess") as int?;
            explorer.ShowStatusBar = key.GetValue("ShowStatusBar") as int?;
            explorer.UseCompactMode = key.GetValue("UseCompactMode") as int?;
            explorer.AutoCheckSelect = key.GetValue("AutoCheckSelect") as int?;
            explorer.NavPaneExpandToCurrentFolder = key.GetValue("NavPaneExpandToCurrentFolder") as int?;
            explorer.ShowSecondsInClock = key.GetValue("ShowSecondsInSystemClock") as int?;
            explorer.StartTrackDocs = key.GetValue("Start_TrackDocs") as int?;
            explorer.StartShowFrequent = key.GetValue("Start_ShowFrequentList") as int?;
            explorer.StartShowRecent = key.GetValue("Start_ShowRecentList") as int?;
            explorer.SnapAssist = key.GetValue("SnapAssist") as int?;
            explorer.EnableSnapBar = key.GetValue("EnableSnapBar") as int?;
            explorer.EnableSnapAssistFlyout = key.GetValue("EnableSnapAssistFlyout") as int?;
            explorer.TaskbarEndTask = key.GetValue("TaskbarEndTask") as int?;
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegClipboard);
            explorer.EnableClipboardHistory = key?.GetValue("EnableClipboardHistory") as int?;
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegExplorerSerialize);
            explorer.SerializeStartupDelay = key?.GetValue("StartupDelayInMSec") as int?;
        }, "reading");
        return explorer;
    }

    public void Write(ExplorerSettings explorer)
    {
        bool changed = false;
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegExplorerAdvanced);

            void Set(string name, int? val)
            {
                if (val.HasValue)
                {
                    key.SetValue(name, val.Value, RegistryValueKind.DWord);
                    changed = true;
                }
            }

            Set("Hidden", explorer.ShowHiddenFiles);
            Set("HideFileExt", explorer.ShowFileExtensions);
            Set("ShowSuperHidden", explorer.ShowSuperHidden);
            Set("FullPath", explorer.ShowFullPathInTitleBar);
            Set("SeparateProcess", explorer.LaunchFolderInSeparateProcess);
            Set("ShowStatusBar", explorer.ShowStatusBar);
            Set("UseCompactMode", explorer.UseCompactMode);
            Set("AutoCheckSelect", explorer.AutoCheckSelect);
            Set("NavPaneExpandToCurrentFolder", explorer.NavPaneExpandToCurrentFolder);
            Set("ShowSecondsInSystemClock", explorer.ShowSecondsInClock);
            Set("Start_TrackDocs", explorer.StartTrackDocs);
            Set("Start_ShowFrequentList", explorer.StartShowFrequent);
            Set("Start_ShowRecentList", explorer.StartShowRecent);
            Set("SnapAssist", explorer.SnapAssist);
            Set("EnableSnapBar", explorer.EnableSnapBar);
            Set("EnableSnapAssistFlyout", explorer.EnableSnapAssistFlyout);
            Set("TaskbarEndTask", explorer.TaskbarEndTask);
        }, "writing");
        safe.Try(() =>
        {
            if (explorer.EnableClipboardHistory.HasValue)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegClipboard);
                key.SetValue("EnableClipboardHistory", explorer.EnableClipboardHistory.Value, RegistryValueKind.DWord);
                changed = true;
            }
        }, "writing");
        safe.Try(() =>
        {
            if (explorer.SerializeStartupDelay.HasValue)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegExplorerSerialize);
                key.SetValue("StartupDelayInMSec", explorer.SerializeStartupDelay.Value, RegistryValueKind.DWord);
                changed = true;
            }
        }, "writing");
        if (changed)
            broadcast.Broadcast();
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.Explorer = Read();

    void ISettingsIO.WriteFrom(UserSettings s) { if (s.Explorer != null) Write(s.Explorer); }
}