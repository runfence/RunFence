using RunFence.Core.Models;

namespace RunFence.Licensing;

public interface ILicenseEvaluationPolicy
{
    FeatureRestrictionResult Evaluate(EvaluationFeature feature, int currentCount, bool isLicensed);
}
