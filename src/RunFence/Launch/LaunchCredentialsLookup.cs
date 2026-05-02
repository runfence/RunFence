using RunFence.Account;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class LaunchCredentialsLookup(
    ISessionProvider sessionProvider,
    ICredentialDecryptionService credentialDecryption)
    : ILaunchCredentialsLookup
{
    public LaunchCredentials GetBySid(string accountSid)
    {
        var session = sessionProvider.GetSession();
        using var scope = session.PinDerivedKey.Unprotect();
        return credentialDecryption.DecryptAndResolve(
                   accountSid, session.CredentialStore, scope.Data,
                   session.Database.SidNames, out var status)
               ?? throw (status == CredentialLookupStatus.NotFound
                   ? new CredentialNotFoundException("Account not found.")
                   : new MissingPasswordException("No password stored for this account."));
    }
}
