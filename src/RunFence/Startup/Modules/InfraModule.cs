using Autofac;
using RunFence.Account;
using RunFence.Infrastructure;

namespace RunFence.Startup.Modules;

public class InfraModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Foundation-scope fallback: all three strategies run synchronously on the calling thread.
        // Session scope (UiModule) overrides this with a real form-dispatching invoker.
        builder.RegisterInstance(new LambdaUiThreadInvoker(a => a(), a => a(), a => a()))
            .As<IUiThreadInvoker>()
            .SingleInstance();

        builder.RegisterType<UiThreadDatabaseAccessor>().AsSelf().SingleInstance();
        builder.RegisterType<ModalTracker>().As<IModalTracker>().SingleInstance();
        builder.RegisterType<AppIconProvider>().As<IAppIconProvider>().SingleInstance();
        builder.RegisterType<SystemTimeProvider>().As<ITimeProvider>().SingleInstance();
        builder.RegisterType<WinFormsTimerScheduler>().As<ITimerScheduler>().InstancePerDependency();
        builder.RegisterType<SystemStopwatchProvider>().As<IStopwatchProvider>().SingleInstance();
        builder.RegisterType<ProcessListService>().As<IProcessListService>().SingleInstance();
        builder.RegisterType<ProcessTerminationService>().As<IProcessTerminationService>().SingleInstance();
        builder.RegisterType<IdleMonitorService>().As<IIdleMonitorService>().SingleInstance();
        builder.RegisterType<PreviousWindowTracker>().As<IPreviousWindowTracker>().SingleInstance();
        builder.RegisterType<InteractiveUserDesktopProvider>().As<IInteractiveUserDesktopProvider>().SingleInstance();
        builder.RegisterType<ShellHelper>().AsSelf().SingleInstance();
    }
}