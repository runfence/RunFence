using RunFence.Launch;

namespace RunFence.Account;

/// <summary>
/// Opens system dialogs related to user account management.
/// Extracted from <see cref="WindowsAccountService"/> to separate the launch concern.
/// </summary>
public class SystemDialogLauncher(ILaunchFacade launchFacade) : ISystemDialogLauncher
{
    public void OpenUserAccountsDialog()
    {
        var lusrmgr = Path.Combine(Environment.SystemDirectory, "lusrmgr.msc");
        if (File.Exists(lusrmgr))
            launchFacade.LaunchFile("mmc.exe", AccountLaunchIdentity.CurrentAccountElevated, "lusrmgr.msc")?.Dispose();
        else
            launchFacade.LaunchFile("control.exe", AccountLaunchIdentity.CurrentAccountElevated, "userpasswords2")?.Dispose();
    }
}
