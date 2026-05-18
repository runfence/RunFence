using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Licensing;

public class LicenseEvaluationPolicy : ILicenseEvaluationPolicy
{
    public FeatureRestrictionResult Evaluate(EvaluationFeature feature, int currentCount, bool isLicensed)
    {
        var limit = feature switch
        {
            EvaluationFeature.Apps => EvaluationConstants.EvaluationMaxApps,
            EvaluationFeature.Containers => EvaluationConstants.EvaluationMaxContainers,
            EvaluationFeature.HiddenAccounts => EvaluationConstants.EvaluationMaxHiddenAccounts,
            EvaluationFeature.Credentials => EvaluationConstants.EvaluationMaxCredentials,
            EvaluationFeature.FirewallAllowlist => EvaluationConstants.EvaluationMaxFirewallAllowlistEntries,
            _ => 0
        };

        var allowed = isLicensed || currentCount < limit;
        return new FeatureRestrictionResult(allowed, feature.ToString(), currentCount, currentCount + 1, limit, feature.ToString());
    }
}
