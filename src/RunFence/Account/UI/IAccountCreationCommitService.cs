using RunFence.Account.UI.Forms;
using RunFence.Core.Models;

namespace RunFence.Account.UI;

public interface IAccountCreationCommitService
{
    AccountCreationCommitResult? Commit(EditAccountDialog dialog, AppDatabase database);
}
