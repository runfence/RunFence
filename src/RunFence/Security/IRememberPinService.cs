using RunFence.Core;

namespace RunFence.Security;

public interface IRememberPinService
{
    bool IsEnabled { get; }                                   // startkey.dat exists
    bool IsTpmAvailable();                                    // delegates to ITpmKeyProvider
    bool TryDecryptSecret(out SecureSecret? pinDerivedKey);   // TPM+DPAPI -> key (startup)
    void EnableWithTpm(ISecureSecretSnapshotSource pinDerivedKey);
    void EnableDpapiOnly(ISecureSecretSnapshotSource pinDerivedKey);
    void Disable();                                           // delete startkey.dat + TPM key
    void UpdateForPinChange(ISecureSecretSnapshotSource newPinDerivedKey); // re-wrap preserving current mode
}
