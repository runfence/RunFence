using RunFence.Account.UI;
using RunFence.Account.UI.Forms;

namespace RunFence.RunAs;

public sealed record ShowCreateAccountResult(
    IShowCreateAccountResultDialog? Dialog,
    bool WasCancelled,
    CreateAccountStatus? Status = null,
    string? ErrorMessage = null);
