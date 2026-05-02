using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Security.UI.Forms;

namespace RunFence.Startup;

/// <summary>
/// Wires InputInjectionBlocker-specific event subscriptions during application startup.
/// </summary>
public class InputInjectionBlockerEventWirer(
    IInputInjectionTraySink trayIconManager,
    IInputInjectionBlockerService injectionBlocker,
    ISessionProvider sessionProvider,
    ISessionSaver sessionSaver,
    IApplicationDataChangeSource applicationState) : IStartupEventWirer
{
    public void WireEvents()
    {
        injectionBlocker.ApplyConfigSetting(sessionProvider.GetSession().Database.Settings.BlockInputInjection);
        injectionBlocker.UpdateExemptedSids(GetExemptedSids());
        trayIconManager.InputInjectionToggleRequested += OnToggle;
        applicationState.DataChanged += OnDataChanged;
    }

    private void OnToggle()
    {
        if (injectionBlocker.IsEnabled)
        {
            var dialog = new DisableBlockingDialog();
            dialog.ShowDialog();
            switch (dialog.Choice)
            {
                case DisableBlockingChoice.UntilRestart:
                    injectionBlocker.SetTemporarilyDisabled();
                    break;
                case DisableBlockingChoice.ForTenMinutes:
                    injectionBlocker.SetTimedDisable(TimeSpan.FromMinutes(10));
                    break;
                case DisableBlockingChoice.Permanently:
                    sessionProvider.GetSession().Database.Settings.BlockInputInjection = false;
                    sessionSaver.SaveConfig();
                    injectionBlocker.ApplyConfigSetting(false);
                    break;
                case DisableBlockingChoice.Cancelled:
                    return;
            }
        }
        else
        {
            sessionProvider.GetSession().Database.Settings.BlockInputInjection = true;
            sessionSaver.SaveConfig();
            injectionBlocker.ReEnable();
            injectionBlocker.ApplyConfigSetting(true);
        }

        trayIconManager.UpdateDatabase(sessionProvider.GetSession().CredentialStore);
    }

    private void OnDataChanged()
    {
        injectionBlocker.ApplyConfigSetting(sessionProvider.GetSession().Database.Settings.BlockInputInjection);
        injectionBlocker.UpdateExemptedSids(GetExemptedSids());
    }

    private IReadOnlyCollection<string> GetExemptedSids()
        => sessionProvider.GetSession().Database.Accounts
            .Where(a => a.ReceiveInjectedInput)
            .Select(a => a.Sid)
            .ToList();
}
