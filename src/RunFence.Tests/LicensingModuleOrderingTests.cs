using Autofac.Core;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class LicensingModuleOrderingTests
{
    [Fact]
    public void LicenseService_Registration_HasRequiresInitializationOrderZero()
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

        var initRegistrations = sessionScope.ComponentRegistry.RegistrationsFor(
            new TypedService(typeof(IRequiresInitialization))).ToList();
        Assert.NotEmpty(initRegistrations);
        var licenseInit = initRegistrations.FirstOrDefault(r => r.Activator.LimitType == typeof(LicenseService));
        Assert.NotNull(licenseInit);
        Assert.True(licenseInit!.Metadata.TryGetValue("Order", out var orderValue));
        Assert.Equal(0, Convert.ToInt32(orderValue));
    }
}
