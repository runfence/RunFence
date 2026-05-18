namespace RunFence.Licensing;

public interface ILicenseMessageFormatter
{
    string FormatRestrictionMessage(FeatureRestrictionResult restriction);
}
