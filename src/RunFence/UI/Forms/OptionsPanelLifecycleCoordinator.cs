using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.UI.Forms;

public sealed class OptionsPanelLifecycleCoordinator(
    OptionsPanelDataLoader dataLoader,
    ILoggingService log)
{
    private IOptionsPanelLifecycleView _view = null!;

    public void Initialize(IOptionsPanelLifecycleView view)
    {
        if (_view != null)
            throw new InvalidOperationException("OptionsPanelLifecycleCoordinator.Initialize can only be called once.");

        _view = view;
    }

    public void BuildDynamicContent()
    {
        GetView().BuildDynamicContent();
    }

    public async Task OnDataSet(AppDatabase database)
    {
        var view = GetView();
        try
        {
            view.SetCallerSidNames(database.SidNames, (sid, name) => database.UpdateSidName(sid, name));

            var (state, settingsChangedByLicense) = await dataLoader.LoadSettingsAsync(database.Settings);
            if (view.IsDisposed)
                return;

            view.ApplyLoadedState(
                state,
                startWithoutPinEnabled: view.StartWithoutPinEnabled,
                blockIcmpWhenInternetBlocked: database.Settings.BlockIcmpWhenInternetBlocked);

            if (settingsChangedByLicense)
                view.SaveSettings();

            HandleDataChanged();
        }
        catch (Exception ex)
        {
            log.Error("OptionsPanel.OnDataSet failed", ex);
            log.Warn($"OptionsPanel load presentation error: {ex.Message}");
        }
    }

    public void HandleDataChanged()
    {
        GetView().RefreshCallerAndConfigLists();
    }

    private IOptionsPanelLifecycleView GetView()
        => _view ?? throw new InvalidOperationException("OptionsPanelLifecycleCoordinator must be initialized before use.");
}
