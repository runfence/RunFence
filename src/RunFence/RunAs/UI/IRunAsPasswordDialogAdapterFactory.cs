namespace RunFence.RunAs.UI;

public interface IRunAsPasswordDialogAdapterFactory
{
    IRunAsPasswordDialogAdapter Create(
        string accountDisplayName,
        bool allowRememberPassword,
        string accountSid,
        string usernameFallback);
}
