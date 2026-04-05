using Autofac;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Startup;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Verifies that the full AutoFac DI graph resolves without errors.
/// Resolving MainForm transitively exercises the entire session-scope dependency graph.
/// No Start()/Initialize() calls are made — constructors are side-effect-free.
/// </summary>
public class ContainerRegistrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void FullGraph_Resolves_Without_Errors()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();

                using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
                var session = new SessionContext
                {
                    Database = new AppDatabase(),
                    CredentialStore = new CredentialStore(),
                    PinDerivedKey = pinKey
                };

                using var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(
                    foundationContainer, session, new StartupOptions(false));

                // Resolve IRequiresInitialization services (same as Program.cs does
                // before MainForm — verifies LicenseService, GlobalHotkeyService, etc.)
                var initServices = sessionScope.Resolve<IOrderedEnumerable<IRequiresInitialization>>();
                Assert.NotEmpty(initServices);

                // Resolving MainForm transitively exercises the full session-scope graph
                var mainForm = sessionScope.Resolve<MainForm>();
                mainForm.Dispose();

                // Verify AppLifecycleStarter (lifecycle wiring) also resolves
                sessionScope.Resolve<AppLifecycleStarter>();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }
}