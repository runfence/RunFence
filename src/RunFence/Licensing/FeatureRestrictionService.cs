using RunFence.Core.Models;

namespace RunFence.Licensing;

public class FeatureRestrictionService(ILicenseEvaluationPolicy policy) : IFeatureRestrictionService
{
    public FeatureRestrictionResult GetRestriction(EvaluationFeature feature, int currentCount, bool isLicensed)
        => policy.Evaluate(feature, currentCount, isLicensed);
}
