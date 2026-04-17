using System.Security;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Launch;

public readonly record struct LaunchCredentials(
    SecureString? Password,
    string? Domain,
    string? Username,
    LaunchTokenSource TokenSource = LaunchTokenSource.Credentials)
{
    public static LaunchCredentials CurrentAccount => new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess);
    public static LaunchCredentials InteractiveUser => new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser);

    public static LaunchCredentials FromCredentialEntry(
        SecureString? password, CredentialEntry credEntry,
        ISidResolver sidResolver, IReadOnlyDictionary<string, string>? sidNames)
    {
        var tokenSource = credEntry.IsCurrentAccount ? LaunchTokenSource.CurrentProcess
            : credEntry.IsInteractiveUser ? LaunchTokenSource.InteractiveUser
            : LaunchTokenSource.Credentials;
        var (domain, username) = SidNameResolver.ResolveDomainAndUsername(credEntry, sidResolver, sidNames);
        return new LaunchCredentials(password, domain, username, tokenSource);
    }
}