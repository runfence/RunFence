using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Infrastructure;
using RunFence.Startup;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundMarkerModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ForegroundPrivilegeMarkerService>()
            .As<IForegroundPrivilegeMarkerService>()
            .As<IForegroundPrivilegeMarkerStateSource>()
            .SingleInstance();
        builder.RegisterType<ForegroundPrivilegeMarkerRuntime>()
            .As<IForegroundPrivilegeMarkerRuntime>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<ForegroundMarkerThreadDispatcher>()
            .As<IForegroundMarkerThreadDispatcher>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<ForegroundWinEventListener>()
            .As<IForegroundWinEventListener>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<WinEventHookApi>()
            .As<IWinEventHookApi>()
            .SingleInstance();
        builder.RegisterType<ForegroundPrivilegeRefreshCoordinator>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<ForegroundShellWindowFilter>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<ForegroundPrivilegeClassificationWorker>()
            .As<IForegroundPrivilegeClassificationWorker>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<ForegroundPrivilegeMarkerMetadataResolver>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<ForegroundPrivilegeClassifier>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<ForegroundWindowBoundsReader>()
            .As<IForegroundWindowBoundsReader>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<ForegroundMonitorIntersectionService>()
            .As<IForegroundMonitorIntersectionService>()
            .SingleInstance();
        builder.RegisterType<ForegroundMarkerNativeMethods>()
            .As<IForegroundMarkerNativeMethods>()
            .SingleInstance();
        builder.RegisterType<DwmWindowFrameBoundsReader>()
            .As<IWindowFrameBoundsReader>()
            .SingleInstance();
        builder.RegisterType<ForegroundMarkerWindow>()
            .As<IForegroundMarkerWindow>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<ForegroundProcessJobInspector>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<JobKeeperStartupReconnectForegroundRefreshBridge>()
            .As<IRequiresInitialization>()
            .OrderBy(4)
            .SingleInstance();
        builder.RegisterType<ForegroundPrivilegeMarkerStartupEventWirer>()
            .As<IStartupEventWirer>()
            .OrderBy(6)
            .SingleInstance();
    }
}
