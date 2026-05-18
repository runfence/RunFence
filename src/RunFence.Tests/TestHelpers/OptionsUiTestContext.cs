using Autofac;
using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup;
using RunFence.Startup.UI;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Tests.TestHelpers;

public sealed class OptionsUiTestContext : IDisposable
{
    private readonly Mock<IRememberPinService> _rememberPinService;
    private readonly ILifetimeScope _sessionScope;

    private OptionsUiTestContext(
        IContainer foundationContainer,
        ILifetimeScope sessionScope,
        ILifetimeScope scope,
        SessionContext session,
        ISecureSecretSnapshotSource initialPinKey,
        ISecureSecretSnapshotSource initialCurrentPinKey,
        Mock<IAutoStartService> autoStartService,
        Mock<IRememberPinService> rememberPinService)
    {
        FoundationContainer = foundationContainer;
        _sessionScope = sessionScope;
        Scope = scope;
        Session = session;
        InitialPinKey = initialPinKey;
        InitialCurrentPinKey = initialCurrentPinKey;
        AutoStartService = autoStartService;
        _rememberPinService = rememberPinService;
    }

    public IContainer FoundationContainer { get; }
    public ILifetimeScope Scope { get; }
    public SessionContext Session { get; }
    public ISecureSecretSnapshotSource InitialPinKey { get; }
    public ISecureSecretSnapshotSource InitialCurrentPinKey { get; }
    public Mock<IAutoStartService> AutoStartService { get; }
    public bool RememberPinEnabled { get; private set; }

    public static OptionsUiTestContext Create(byte rotatedKeyByte)
    {
        var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();
        var autoStartService = new Mock<IAutoStartService>();
        var rememberPinService = new Mock<IRememberPinService>();
        var promptService = new Mock<IStartWithoutPinPromptService>();
        var rotationRunner = new Mock<IStartWithoutPinRotationRunner>();
        var licenseService = new Mock<ILicenseService>();
        var appConfigService = new Mock<IAppConfigService>();
        var pinService = new Mock<IPinService>();

        promptService.Setup(service => service.ConfirmSecurityWarning()).Returns(true);
        rememberPinService.Setup(service => service.IsTpmAvailable()).Returns(true);
        licenseService.SetupGet(service => service.IsLicensed).Returns(true);
        autoStartService.Setup(service => service.IsAutoStartEnabled()).ReturnsAsync(false);

        var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(TestSecretFactory.Create(32));

        rotationRunner
            .Setup(runner => runner.Run(It.IsAny<string>(), It.IsAny<SessionContext>()))
            .Returns(() => new PinKeyRotationResult(
                new CredentialStore(),
                new SecureSecret(32, data => data.Fill(rotatedKeyByte))));

        var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(
            foundationContainer,
            session,
            new StartupOptions(false, false));

        var scope = sessionScope.BeginLifetimeScope(builder =>
        {
            builder.RegisterInstance(autoStartService.Object).As<IAutoStartService>();
            builder.RegisterInstance(rememberPinService.Object).As<IRememberPinService>();
            builder.RegisterInstance(promptService.Object).As<IStartWithoutPinPromptService>();
            builder.RegisterInstance(rotationRunner.Object).As<IStartWithoutPinRotationRunner>();
            builder.RegisterInstance(licenseService.Object).As<ILicenseService>();
            builder.RegisterInstance(appConfigService.Object).As<IAppConfigService>();
            builder.RegisterInstance(pinService.Object).As<IPinService>();
            builder.RegisterType<PinChangeOrchestrator>().AsSelf().SingleInstance();
            builder.RegisterType<OptionsStartWithoutPinHandler>().AsSelf().SingleInstance();
            builder.RegisterType<OptionsPanelDataLoader>().AsSelf().SingleInstance();
            builder.RegisterType<OptionsPanel>().AsSelf().InstancePerDependency();
            builder.RegisterType<MainForm>().AsSelf().InstancePerDependency();
        });

        var context = new OptionsUiTestContext(
            foundationContainer,
            sessionScope,
            scope,
            session,
            session.PinDerivedKey,
            session.PinDerivedKey,
            autoStartService,
            rememberPinService);
        context.SetRememberPinEnabled(false);
        return context;
    }

    public void SetRememberPinEnabled(bool enabled)
    {
        RememberPinEnabled = enabled;
        _rememberPinService.SetupGet(service => service.IsEnabled).Returns(() => RememberPinEnabled);
        _rememberPinService
            .Setup(service => service.EnableWithTpm(It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback(() => RememberPinEnabled = true);
        _rememberPinService
            .Setup(service => service.EnableDpapiOnly(It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback(() => RememberPinEnabled = true);
        _rememberPinService
            .Setup(service => service.Disable())
            .Callback(() => RememberPinEnabled = false);
    }

    public void Dispose()
    {
        Scope.Dispose();
        _sessionScope.Dispose();
        Session.Dispose();
        FoundationContainer.Dispose();
    }
}
