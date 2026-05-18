using System.Runtime.InteropServices;
using RunFence.Acl;
using RunFence.Launch;

namespace RunFence.Infrastructure;

public class ShellHelper(
    ILaunchFacade launchFacade,
    ILaunchFeedbackPresenter launchFeedbackPresenter)
    : IShellHelper
{
    public void ShowProperties(string path, IWin32Window? owner = null)
    {
        var info = new ShellNative.ShellExecuteExInfo
        {
            cbSize = Marshal.SizeOf<ShellNative.ShellExecuteExInfo>(),
            fMask = 0xC, // SEE_MASK_INVOKEIDLIST
            hwnd = owner?.Handle ?? IntPtr.Zero,
            lpVerb = "properties",
            lpFile = path,
            nShow = 5 // SW_SHOW
        };
        ShellNative.ShellExecuteEx(ref info);
    }

    public void OpenInExplorer(string path)
    {
        try
        {
            using var launch = launchFacade.LaunchFile("explorer.exe", AccountLaunchIdentity.CurrentAccountElevated, $"\"{path}\"");
            launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("Explorer", LaunchFeedbackSource.InteractiveUi));
        }
        catch (OperationCanceledException)
        {
        }
        catch (GrantOperationException ex)
        {
            launchFeedbackPresenter.ShowGrantFailure(ex, new LaunchFeedbackContext("Explorer", LaunchFeedbackSource.InteractiveUi));
        }
    }

    public void OpenDefaultAppsSettings()
    {
        try
        {
            using var launch = launchFacade.LaunchUrl("ms-settings:defaultapps", AccountLaunchIdentity.CurrentAccountElevated);
            launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("Default Apps Settings", LaunchFeedbackSource.InteractiveUi));
        }
        catch (OperationCanceledException)
        {
        }
        catch (AssociationResolutionException ex)
        {
            MessageBox.Show(
                $"RunFence could not open Default Apps settings because Windows has no usable association handler for 'ms-settings:defaultapps'.\n\n{ex.Message}",
                "Default Apps Settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (GrantOperationException ex)
        {
            launchFeedbackPresenter.ShowGrantFailure(ex, new LaunchFeedbackContext("Default Apps Settings", LaunchFeedbackSource.InteractiveUi));
        }
    }

    public void OpenUrlAsInteractiveUser(string url)
    {
        try
        {
            using var launch = launchFacade.LaunchUrl(
                url,
                AccountLaunchIdentity.InteractiveUser with
                {
                    AssociationResolutionPolicy = AssociationResolutionPolicy.AllowAccountRedirection
                });
            launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("The URL handler", LaunchFeedbackSource.InteractiveUi));
        }
        catch (OperationCanceledException)
        {
        }
        catch (GrantOperationException ex)
        {
            launchFeedbackPresenter.ShowGrantFailure(ex, new LaunchFeedbackContext("The URL handler", LaunchFeedbackSource.InteractiveUi));
        }
    }
}
