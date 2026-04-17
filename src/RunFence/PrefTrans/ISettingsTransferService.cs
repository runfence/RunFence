namespace RunFence.PrefTrans;

public interface ISettingsTransferService
{
    SettingsTransferResult ExportDesktopSettings(string outputFilePath, int timeoutMs = 30_000, Action? pollCallback = null);

    SettingsTransferResult Import(string settingsFilePath, string accountSid,
        int timeoutMs = 60_000, Action? pollCallback = null);
}