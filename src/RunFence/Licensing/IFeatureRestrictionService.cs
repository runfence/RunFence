using RunFence.Core.Models;

namespace RunFence.Licensing;

public interface IFeatureRestrictionService
{
    FeatureRestrictionResult GetRestriction(EvaluationFeature feature, int currentCount, bool isLicensed);
}
