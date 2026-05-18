namespace RunFence.Core.Models;

public enum LicenseActivationResult
{
    Success,
    InvalidSignature,
    WrongVersion,
    WrongMachine,
    MachineIdentityUnavailable,
    PersistenceFailed,
    Expired,
    Malformed
}
