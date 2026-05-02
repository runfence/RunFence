using RunFence.Core;

namespace RunFence.Account;

public interface IAccountPasswordService
{
    void ChangeAccountPassword(string sid, ProtectedString oldPassword, ProtectedString newPassword);
    void AdminResetAccountPassword(string sid, ProtectedString newPassword);
}