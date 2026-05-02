using RunFence.Core.Models;

namespace RunFence.Wizard;

public interface ICredentialDialogRunner
{
    CredentialDialogResult ShowCredentialDialog(
        CredentialEntry credentialEntry,
        IReadOnlyDictionary<string, string>? sidNames);
}
