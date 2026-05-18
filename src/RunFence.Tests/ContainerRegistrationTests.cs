using Autofac;
using Autofac.Core;
using Microsoft.Win32;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core.Helpers;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using RunFence.Licensing;
using RunFence.RunAs.UI.Forms;
using RunFence.SidMigration;
using RunFence.Startup;
using RunFence.Startup.NonElevatedMocks;
using RunFence.Startup.UI;
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
        var stage = "not started";
        var thread = new Thread(() =>
        {
            try
            {
                stage = "build foundation container";
                using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();

                // Verify foundation-scope startup orchestration resolves before session exists.
                // Program.Main resolves StartupOrchestrator from the foundation container.
                stage = "resolve startup orchestrator";
                foundationContainer.Resolve<StartupOrchestrator>();

                using var pinKey = TestSecretFactory.Create(32);
                var session = new SessionContext
{
                    Database = new AppDatabase(),
                    CredentialStore = new CredentialStore(),
                }.WithOwnedPinDerivedKey(pinKey);

                stage = "begin session scope";
                using var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(
                    foundationContainer, session, new StartupOptions(false, false));

                // Assert registration ordering contract for licensing initialization without
                // forcing eager activation of all init services (which can include independent
                // circular graphs while other refactors are in progress).
                var initRegistrations = sessionScope.ComponentRegistry.RegistrationsFor(
                    new TypedService(typeof(IRequiresInitialization))).ToList();
                Assert.NotEmpty(initRegistrations);
                var licenseInit = initRegistrations.FirstOrDefault(r => r.Activator.LimitType == typeof(LicenseService));
                Assert.NotNull(licenseInit);
                Assert.True(licenseInit!.Metadata.TryGetValue("Order", out var orderValue));
                Assert.Equal(0, Convert.ToInt32(orderValue));

                // Resolving MainForm transitively exercises the full session-scope graph
                var mainForm = sessionScope.Resolve<MainForm>();
                mainForm.Dispose();

                // Verify AppLifecycleStarter (lifecycle wiring) also resolves
                sessionScope.Resolve<AppLifecycleStarter>();
                var fallbackPrompt = sessionScope.Resolve<IWindowsHelloPinFallbackPrompt>();
                var fallbackPromptSource = sessionScope.Resolve<IWindowsHelloPinFallbackPromptEventSource>();
                Assert.Same(fallbackPrompt, fallbackPromptSource);

                // Explicitly resolve types that are only created via Func<T> factory delegates —
                // these escape the MainForm transitive resolution above and would otherwise hide
                // scope mismatches (e.g. a SingleInstance in foundation depending on a session-scope type).
                using var appEditDialog = sessionScope.Resolve<AppEditDialog>();
                sessionScope.Resolve<ILaunchTargetResolver>();
                sessionScope.Resolve<IPreparedTokenProcessLauncher>();
                sessionScope.Resolve<InAppMigrationHandler>();

                var fallbackRegistryFactory = sessionScope.Resolve<Func<RegistryKey, AssociationFallbackRegistry>>();
                Assert.NotNull(fallbackRegistryFactory);
                stage = "build fallback registry";
                var fallbackRegistry = fallbackRegistryFactory(Registry.Users);
                Assert.NotNull(fallbackRegistry);

                var fallbackRestoreFactory = sessionScope.Resolve<Func<IAssociationFallbackRegistry, AssociationFallbackRestoreService>>();
                Assert.NotNull(fallbackRestoreFactory);
                var fallbackRestoreService = fallbackRestoreFactory(fallbackRegistry);
                Assert.NotNull(fallbackRestoreService);

                var defaultFallbackRegistry = sessionScope.Resolve<IAssociationFallbackRegistry>();
                Assert.IsType<AssociationFallbackRegistry>(defaultFallbackRegistry);
                var defaultFallbackRegistrySecond = sessionScope.Resolve<IAssociationFallbackRegistry>();
                Assert.NotSame(defaultFallbackRegistry, defaultFallbackRegistrySecond);

                stage = "resolved full graph";
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), $"Timeout while resolving full DI graph at stage '{stage}'.");
        Assert.Null(error);
    }

    [Fact]
    public void Stage1A_PublicConstructorsForUiProductionTypes()
    {
        using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();
        using var pinKey = TestSecretFactory.Create(32);
        var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(pinKey);

        using var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(
            foundationContainer, session, new StartupOptions(false, false));

        Assert.NotNull(sessionScope.Resolve<AppEditBrowseHelper>());
        Assert.NotNull(sessionScope.Resolve<AppEditDialogController>());
        using var appEditDialog = sessionScope.Resolve<AppEditDialog>();
        Assert.NotNull(appEditDialog);
        using var runAsDialog = sessionScope.Resolve<RunAsDialog>();
        Assert.NotNull(runAsDialog);
    }

    [Fact]
    public void LocalGroupServices_ResolveWithDebugMockDecorators_WhenAdminMocksEnabled()
    {
        if (!DebugHelper.UseAdminOperationMocks)
            return;

        using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();
        using var pinKey = TestSecretFactory.Create(32);
        var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(pinKey);

        using var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(
            foundationContainer, session, new StartupOptions(false, false));

        var query = sessionScope.Resolve<ILocalGroupQueryService>();
        var mutation = sessionScope.Resolve<ILocalGroupMutationService>();
        var membership = sessionScope.Resolve<ILocalGroupMembershipService>();

        Assert.IsType<MockLocalGroupQueryService>(query);
        Assert.IsType<MockLocalGroupMutationService>(mutation);

        var sid = mutation.CreateGroup("ContainerRegistrationTests.MockGroup", "mock");
        var queryGroups = query.GetLocalGroups();
        var combinedGroups = membership.GetLocalGroups();

        Assert.Contains(queryGroups, g => string.Equals(g.Sid, sid, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(combinedGroups, g => string.Equals(g.Sid, sid, StringComparison.OrdinalIgnoreCase));
    }
}
