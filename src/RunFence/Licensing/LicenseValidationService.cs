using RunFence.Core.Models;

namespace RunFence.Licensing;

internal class LicenseValidationService(IMachineIdProvider machineIdProvider, LicenseValidator validator) : ILicenseValidator
{
    public LicenseValidationResult Validate(string? keyString, DateTime today)
    {
        var identity = machineIdProvider.GetMachineIdentity();
        if (identity.Status != MachineIdentityStatus.Available || identity.MachineIdHash == null)
            return new LicenseValidationResult(
                LicenseValidationStatus.MachineIdentityUnavailable,
                LicenseInfo.Unlicensed,
                identity.Status,
                identity.ErrorText ?? "Machine identity unavailable.");

        var (result, info) = validator.Validate(keyString, identity.MachineIdHash, today);
        var status = result switch
        {
            LicenseActivationResult.Success => LicenseValidationStatus.Valid,
            LicenseActivationResult.WrongVersion => LicenseValidationStatus.WrongVersion,
            LicenseActivationResult.Expired => LicenseValidationStatus.Expired,
            LicenseActivationResult.InvalidSignature => LicenseValidationStatus.SignatureInvalid,
            LicenseActivationResult.WrongMachine => LicenseValidationStatus.MachineMismatch,
            LicenseActivationResult.Malformed => LicenseValidationStatus.CorruptData,
            _ => LicenseValidationStatus.Failed
        };

        return new LicenseValidationResult(status, info, identity.Status, status == LicenseValidationStatus.Valid ? null : status.ToString());
    }
}
