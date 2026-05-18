using Autofac;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.UI.Forms;

namespace RunFence.Startup;

/// <summary>
/// Production implementation of <see cref="IStartupMainFormRunner"/>.
/// Resolves session-scoped services from the provided lifetime scope, initializes
/// the license service, starts the application lifecycle, runs the WinForms
/// message loop, and unregisters all folder handlers after the message loop exits.
/// </summary>
public class StartupMainFormRunner : IStartupMainFormRunner
{
    public void Run(ILifetimeScope sessionScope)
    {
        // Initialize license before MainForm — its constructor reads IsLicensed for the title
        // and About panel. Other init services run later in AppLifecycleStarter.Start().
        sessionScope.Resolve<ILicenseService>().Initialize();

        var mainForm = sessionScope.Resolve<MainForm>();
        var lifecycleStarter = sessionScope.Resolve<AppLifecycleStarter>();
        var folderHandlerService = sessionScope.Resolve<IFolderHandlerService>();

        lifecycleStarter.Start();

        Application.Run(mainForm);

        folderHandlerService.UnregisterAll();
    }
}
