using RunFence.Account.UI.Forms;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Groups.UI.Forms;
using RunFence.Licensing.UI.Forms;
using RunFence.Persistence.UI;

namespace RunFence.UI.Forms;

public sealed class MainFormContentCoordinator(
    ApplicationsPanel appsPanel,
    AccountsPanel accountsPanel,
    GroupsPanel groupsPanel,
    OptionsPanel optionsPanel,
    MainFormTrayHandler trayHandler,
    SessionContext session)
{
    private IMainFormContentView _view = null!;
    private bool _initialTabSelectionApplied;
    private bool _tabsBuilt;

    public void Initialize(IMainFormContentView view)
    {
        if (_view != null)
            throw new InvalidOperationException("MainFormContentCoordinator.Initialize can only be called once.");

        _view = view;
    }

    public void BuildTabs(AboutPanel aboutPanel)
    {
        if (_tabsBuilt)
            throw new InvalidOperationException("MainFormContentCoordinator.BuildTabs can only be called once.");

        var view = GetView();

        appsPanel.DataChanged += HandleDataChanged;
        appsPanel.EnforcementRequested += view.RequestEnforcement;
        appsPanel.AccountNavigationRequested += view.NavigateToAccount;
        view.AttachApplicationsPanel(appsPanel);

        accountsPanel.DataChanged += HandleDataChanged;
        accountsPanel.AppNavigationRequested += view.NavigateToApp;
        accountsPanel.NewAppRequested += view.OpenAddDialogForAccount;
        view.AttachAccountsPanel(accountsPanel);

        groupsPanel.GroupsChanged += HandleGroupsChanged;
        view.AttachGroupsPanel(groupsPanel);

        optionsPanel.SettingsChanged += view.HandleOptionsSettingsChanged;
        optionsPanel.PinDerivedKeyChanged += HandlePinDerivedKeyChanged;
        optionsPanel.DataChanged += HandleDataChanged;
        optionsPanel.CleanupRequested += view.RequestCleanup;
        optionsPanel.MigrationExitRequested += view.RequestMigrationExit;
        optionsPanel.ConfigLoadRequested += view.RequestConfigLoad;
        optionsPanel.ConfigUnloadRequested += view.RequestConfigUnload;
        view.AttachOptionsPanel(optionsPanel);

        view.AttachAboutPanel(aboutPanel);
        view.ActivationRefreshRequested += OnActivationRefreshRequested;

        trayHandler.Initialize(view, view.FormControl);
        trayHandler.UpdateTitleAndTooltip();
        _tabsBuilt = true;
    }

    public void SetData(AppDatabase database)
    {
        appsPanel.SetData(session);
        accountsPanel.SetData(session);
        groupsPanel.SetData(session);
        optionsPanel.SetData(session);

        if (!_initialTabSelectionApplied && database.Apps.Count == 0)
            GetView().SelectAccountsTab();

        _initialTabSelectionApplied = true;
    }

    public void HandleDataChanged()
    {
        trayHandler.UpdateTray();
        appsPanel.SetData(session);
        accountsPanel.SetData(session);
        groupsPanel.SetData(session);
        optionsPanel.SetData(session);
        trayHandler.ScheduleDiscoveryRefresh();
    }

    public void HandleGroupsChanged()
    {
        trayHandler.UpdateTray();
        accountsPanel.SetData(session);
        groupsPanel.SetData(session);
        trayHandler.ScheduleDiscoveryRefresh();
    }

    private void HandlePinDerivedKeyChanged()
    {
        SetData(session.Database);
    }

    private void OnActivationRefreshRequested(object? sender, EventArgs e)
    {
        var view = GetView();
        view.ScheduleAvailabilityCheck();
        view.QueueSelectedTabRefresh();
    }

    private IMainFormContentView GetView()
        => _view ?? throw new InvalidOperationException("MainFormContentCoordinator must be initialized before use.");
}
