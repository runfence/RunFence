using RunFence.Core;

namespace RunFence.Account;

public interface IAccountPasswordService
{
    AccountPasswordResult ValidatePassword(string sid, ProtectedString password, string usernameFallback);
    AccountPasswordResult ChangeAccountPassword(string sid, ProtectedString oldPassword, ProtectedString newPassword);
    AccountPasswordResult AdminResetAccountPassword(string sid, ProtectedString newPassword);
}
