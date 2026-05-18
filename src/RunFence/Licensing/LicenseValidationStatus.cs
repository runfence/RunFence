namespace RunFence.Licensing;

public enum LicenseValidationStatus
{
    Valid,
    WrongVersion,
    Expired,
    SignatureInvalid,
    MachineMismatch,
    MachineIdentityUnavailable,
    CorruptData,
    Failed
}
