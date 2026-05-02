using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Licensing;

namespace RunFence.Persistence;

public class MainConfigImportEvaluationValidator(
    ILicenseService licenseService,
    IAppConfigService appConfigService)
{
    public void Validate(AppDatabase database, AppDatabase importedDb)
    {
        if (licenseService.IsLicensed)
            return;

        var violations = new List<string>();
        var additionalAppsCount = database.Apps.Count(app => appConfigService.GetConfigPath(app.Id) != null);
        var totalAfterImport = importedDb.Apps.Count + additionalAppsCount;
        var appsMessage = licenseService.GetRestrictionMessage(EvaluationFeature.Apps, totalAfterImport - 1);
        if (appsMessage != null)
            violations.Add($"Apps: {appsMessage}");

        var totalAllowlistEntries = importedDb.Accounts?.Sum(account => account.Firewall.Allowlist.Count) ?? 0;
        if (totalAllowlistEntries > EvaluationConstants.EvaluationMaxFirewallAllowlistEntries)
        {
            violations.Add(
                $"Firewall whitelist: imported config has {totalAllowlistEntries} entries across all accounts (limit: {EvaluationConstants.EvaluationMaxFirewallAllowlistEntries})");
        }

        if (violations.Count > 0)
        {
            throw new EvaluationLimitException(
                "The imported config exceeds evaluation limits.\n\n" +
                string.Join("\n", violations) +
                "\n\nActivate a license to remove these limits.");
        }
    }
}
