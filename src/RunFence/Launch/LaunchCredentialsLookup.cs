using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class LaunchCredentialsLookup(
    ISessionProvider sessionProvider,
    ICredentialDecryptionService credentialDecryption,
    Func<IUiThreadInvoker> uiThreadInvokerFactory)
    : ILaunchCredentialsLookup
{
    public LaunchCredentials GetBySid(string accountSid)
        => uiThreadInvokerFactory().Invoke(() =>
        {
            var session = sessionProvider.GetSession();
            var credentialStore = session.CredentialStore.CreateSnapshot();
            var sidNames = new Dictionary<string, string>(session.Database.SidNames, StringComparer.OrdinalIgnoreCase);

            return session.PinDerivedKey.TransformSnapshot(key =>
            credentialDecryption.DecryptAndResolve(
                accountSid,
                credentialStore,
                key,
                sidNames,
                out var status)
            ?? throw (status == CredentialLookupStatus.NotFound
                ? new CredentialNotFoundException("Account not found.")
                : new MissingPasswordException("No password stored for this account.")));
        });
}
