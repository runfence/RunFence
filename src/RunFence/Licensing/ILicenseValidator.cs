namespace RunFence.Licensing;

public interface ILicenseValidator
{
    LicenseValidationResult Validate(string? keyString, DateTime today);
}
