using RunFence.Core;

namespace RunFence.Security;

public interface IRememberPinService
{
    bool IsEnabled { get; }                                   // startkey.dat exists
    bool IsTpmAvailable();                                    // delegates to ITpmKeyProvider
    bool TryDecrypt(out byte[] pinDerivedKey);                // TPM+DPAPI -> key (startup)
    void EnableWithTpm(ProtectedBuffer pinDerivedKey);        // create startkey.dat mode=TPM
    void EnableDpapiOnly(ProtectedBuffer pinDerivedKey);      // create startkey.dat mode=DPAPI
    void Disable();                                           // delete startkey.dat + TPM key
    void UpdateForPinChange(ProtectedBuffer newPinDerivedKey); // re-wrap preserving current mode
}
