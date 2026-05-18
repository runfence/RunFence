using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Startup.UI;

public sealed record AccountConfigTransferAuthorizationResult(
    bool Completed,
    ProtectedString? CapturedPassword,
    CredentialStore ReplacementStore);
