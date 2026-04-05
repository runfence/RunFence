using System.Security;

namespace RunFence.Account;

public interface IAccountPasswordService
{
    void ChangeAccountPassword(string sid, SecureString oldPassword, string newPassword);
    void AdminResetAccountPassword(string sid, string newPassword);
}