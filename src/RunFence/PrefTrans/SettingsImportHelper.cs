namespace RunFence.PrefTrans;

public static class SettingsImportHelper
{
    /// <summary>
    /// Async version — runs Import on a background thread. Grant tracking is handled internally by EnsureAccess.
    /// </summary>
    public static async Task<(string? error, bool hadGrants)> ImportAsync(
        string settingsPath, string accountSid,
        ISettingsTransferService settingsTransferService)
    {
        var result = await Task.Run(() =>
            settingsTransferService.Import(settingsPath, accountSid));
        return (result.Success ? null : result.Message, result.DatabaseModified);
    }
}
