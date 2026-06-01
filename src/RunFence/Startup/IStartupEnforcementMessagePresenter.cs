namespace RunFence.Startup;

public interface IStartupEnforcementMessagePresenter
{
    void ShowRepairSaveFailure(string message);

    void ShowSuccess();

    void ShowShortcutWarning(string warningMessage);

    void ShowEnforcementFailure(Exception exception);
}
