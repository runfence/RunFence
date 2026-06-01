using Autofac;
using Autofac.Core;
using RunFence.Core.Models;
using RunFence.ForegroundMarker;
using RunFence.Infrastructure;
using RunFence.Startup;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class ContainerRegistrationRestrictedJobCacheTests
{
    [Fact]
    public void StartupReconnectService_AndForegroundRefreshBridge_AreRegisteredWithRequiredOrdering()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();
            using var pinKey = TestSecretFactory.Create(32);
            var session = new SessionContext
            {
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithClonedPinDerivedKey(pinKey);

            using var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(
                foundationContainer,
                session,
                new StartupOptions(false, false));

            var backgroundServices = sessionScope.Resolve<IOrderedEnumerable<IBackgroundService>>().ToList();
            Assert.Contains(backgroundServices, service => service is JobKeeperStartupReconnectService);
            Assert.IsType<JobKeeperStartupReconnectService>(backgroundServices.First());

            var initServices = sessionScope.Resolve<IOrderedEnumerable<IRequiresInitialization>>().ToList();
            var bridgeIndex = initServices.FindIndex(service => service is JobKeeperStartupReconnectForegroundRefreshBridge);
            var clipboardIndex = initServices.FindIndex(service => service is ClipboardPasteInterceptService);
            Assert.True(bridgeIndex >= 0);
            Assert.True(clipboardIndex >= 0);
            Assert.True(bridgeIndex < clipboardIndex);
        });
    }
}
