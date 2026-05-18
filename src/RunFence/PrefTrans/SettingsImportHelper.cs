namespace RunFence.PrefTrans;

public static class SettingsImportHelper
{
    public static async Task<SettingsImportResult> ImportAsync(
        string settingsPath,
        string accountSid,
        ISettingsTransferService settingsTransferService)
    {
        var result = await Task.Run(() => settingsTransferService.Import(settingsPath, accountSid));
        if (result.Success)
            return new SettingsImportResult(SettingsImportStatus.Succeeded, 1, 0, [], [], [], result.DatabaseModified);

        var status = result.Message.Contains("declined", StringComparison.OrdinalIgnoreCase)
            ? SettingsImportStatus.Canceled
            : SettingsImportStatus.Failed;
        return new SettingsImportResult(status, 0, 0, [], [], [result.Message], result.DatabaseModified);
    }
}
