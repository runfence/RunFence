using RunFence.Account.UI.Forms;

namespace RunFence.RunAs;

public record ShowCreateAccountResult(EditAccountDialog? Dialog, bool WasCancelled);
