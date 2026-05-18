using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Security.UI.Forms;

namespace RunFence.Security;

public class InputInjectionBlockerController(
    IInputInjectionBlockerService blockerService,
    IInputInjectionTraySink traySink,
    ISessionProvider sessionProvider,
    ISessionSaver sessionSaver,
    IApplicationDataChangeSource applicationDataChangeSource,
    IInputInjectionDisableBlockingDialogService disableBlockingDialogService)
    : IRequiresInitialization, IDisposable
{
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        ApplyCurrentSessionState();
        traySink.InputInjectionToggleRequested += OnToggle;
        applicationDataChangeSource.DataChanged += OnDataChanged;
    }

    private void OnToggle()
    {
        var session = sessionProvider.GetSession();
        if (blockerService.IsEnabled)
        {
            switch (disableBlockingDialogService.Show())
            {
                case DisableBlockingChoice.UntilRestart:
                    blockerService.SetTemporarilyDisabled();
                    break;
                case DisableBlockingChoice.ForTenMinutes:
                    blockerService.SetTimedDisable(TimeSpan.FromMinutes(10));
                    break;
                case DisableBlockingChoice.Permanently:
                    session.Database.Settings.BlockInputInjection = false;
                    sessionSaver.SaveConfig();
                    blockerService.ApplyConfigSetting(false);
                    break;
                default:
                    return;
            }
        }
        else
        {
            session.Database.Settings.BlockInputInjection = true;
            sessionSaver.SaveConfig();
            blockerService.ReEnable();
            blockerService.ApplyConfigSetting(true);
        }

        traySink.UpdateDatabase(session.CredentialStore);
    }

    private void OnDataChanged()
    {
        ApplyCurrentSessionState();
    }

    private void ApplyCurrentSessionState()
    {
        var session = sessionProvider.GetSession();
        blockerService.ApplyConfigSetting(session.Database.Settings.BlockInputInjection);
        blockerService.UpdateExemptedSids(session.Database.Accounts
            .Where(a => a.ReceiveInjectedInput)
            .Select(a => a.Sid)
            .ToList());
    }

    public void Dispose()
    {
        traySink.InputInjectionToggleRequested -= OnToggle;
        applicationDataChangeSource.DataChanged -= OnDataChanged;
    }
}
