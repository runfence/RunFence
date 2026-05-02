using RunFence.Core;

namespace RunFence.Wizard;

public sealed record CredentialDialogResult(bool Accepted, ProtectedString? Password);
