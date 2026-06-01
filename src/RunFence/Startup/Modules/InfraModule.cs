using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Account;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launching.Processes;
using RunFence.Persistence;

namespace RunFence.Startup.Modules;

public class InfraModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Foundation-scope fallback: all three strategies run synchronously on the calling thread.
        // Session scope (UiModule) overrides this with a real form-dispatching invoker.
        builder.RegisterInstance(new LambdaUiThreadInvoker(a => a(), a => a()))
            .As<IUiThreadInvoker>()
            .SingleInstance();
        builder.Register(ctx =>
            {
                var sessionProvider = ctx.Resolve<SessionProvider>();
                return new Func<IUiThreadInvoker>(() => sessionProvider.GetSessionScope().Resolve<IUiThreadInvoker>());
            })
            .SingleInstance();
        builder.Register(ctx =>
            {
                var sessionProvider = ctx.Resolve<SessionProvider>();
                return new Func<IMainConfigPersistence>(() => sessionProvider.GetSessionScope().Resolve<IMainConfigPersistence>());
            })
            .SingleInstance();
        builder.Register(ctx =>
            {
                var sessionProvider = ctx.Resolve<SessionProvider>();
                return new Func<ITrackingJobStateStore>(() => sessionProvider.GetSessionScope().Resolve<ITrackingJobStateStore>());
            })
            .SingleInstance();
        builder.RegisterType<NoOpTrayWarningSink>()
            .As<ITrayWarningSink>()
            .SingleInstance();

        builder.RegisterType<UiThreadDatabaseAccessor>().AsSelf().SingleInstance();
        builder.RegisterType<ModalTracker>().As<IModalTracker>().SingleInstance();
        builder.RegisterType<StartupUnlockGrant>().As<IStartupUnlockGrant>().SingleInstance();
        builder.RegisterType<AppIconProvider>().As<IAppIconProvider>().SingleInstance();
        builder.RegisterType<SystemTimeProvider>().As<ITimeProvider>().SingleInstance();
        builder.RegisterType<SystemClock>().As<IClock>().SingleInstance();
        builder.RegisterType<CryptographicRandomSource>().As<IRandomSource>().SingleInstance();
        builder.RegisterType<SystemAsyncDelay>().As<IAsyncDelay>().SingleInstance();
        builder.RegisterType<WinFormsTimerScheduler>().As<ITimerScheduler>().InstancePerDependency();
        builder.RegisterType<SystemStopwatchProvider>().As<IStopwatchProvider>().SingleInstance();
        builder.RegisterType<ProcessSnapshotScanner>()
            .AsSelf()
            .As<IProcessSnapshotEnumerator>()
            .As<IProcessImageNameSnapshotReader>()
            .As<IProcessExecutablePathReader>()
            .As<IProcessOwnerInfoReader>()
            .As<IProcessIntegrityLevelReader>()
            .SingleInstance();
        builder.RegisterType<ProcessExecutionService>().As<IProcessExecutionService>().SingleInstance();
        builder.RegisterType<FileContentService>().As<IFileContentService>().SingleInstance();
        builder.RegisterType<RunFenceLauncherPathProvider>().As<IRunFenceLauncherPathProvider>().SingleInstance();
        builder.RegisterType<KeyboardStateReader>().As<IKeyboardStateReader>().SingleInstance();
        builder.RegisterType<WindowInputApi>().As<IWindowInputApi>().SingleInstance();
        builder.RegisterType<ForegroundWindowResolver>().As<IForegroundWindowResolver>().SingleInstance();
        builder.RegisterType<ProcessIdentityReader>()
            .As<IProcessQueryHandleProvider>()
            .As<IProcessPrivilegeStateReader>()
            .As<IProcessCreationTimeReader>()
            .As<IWindowProcessIdReader>()
            .As<IConsoleHostProcessResolver>()
            .As<IProcessOwnerSidReader>()
            .As<IProcessImagePathReader>()
            .As<IProcessAppContainerSidReader>()
            .As<IProcessIdentitySnapshotReader>()
            .SingleInstance();
        builder.RegisterType<ClipboardFormatReader>().As<IClipboardFormatReader>().SingleInstance();
        builder.RegisterType<ClipboardPayloadBuilder>().As<IClipboardPayloadBuilder>().SingleInstance();
        builder.RegisterType<RemoteProcessInjector>().As<IRemoteProcessInjector>().SingleInstance();
        builder.RegisterType<SyntheticInputSender>().As<ISyntheticInputSender>().InstancePerLifetimeScope();
        builder.RegisterType<ClipboardPasteWorkScheduler>().As<IClipboardPasteWorkScheduler>().SingleInstance();
        builder.RegisterType<LowLevelHookApi>().As<ILowLevelHookApi>().SingleInstance();
        builder.RegisterType<ClipboardPasteKeyDecision>().AsSelf().SingleInstance();
        builder.RegisterType<ClipboardPasteTargetResolver>().As<IClipboardPasteTargetResolver>().SingleInstance();
        builder.RegisterType<WindowsJobObjectNative>().As<IWindowsJobObjectNative>().SingleInstance();
        builder.RegisterType<WindowsJobObjectApi>().As<IJobObjectApi>().SingleInstance();
        builder.RegisterType<ObjectTypeNameReader>().As<IObjectTypeNameReader>().SingleInstance();
        builder.RegisterType<ProcessHandleSnapshotNative>().As<IProcessHandleSnapshotNative>().SingleInstance();
        builder.RegisterType<ProcessHandleSnapshotProvider>().As<IProcessHandleSnapshotProvider>().SingleInstance();
        builder.RegisterType<VerifiedRestrictedJobAdmissionPolicy>().AsSelf().SingleInstance();
        builder.RegisterType<VerifiedRestrictedJobCache>().As<IVerifiedRestrictedJobCache>().SingleInstance();
        builder.RegisterType<JobKeeperStartupReconnectService>()
            .AsSelf()
            .As<IJobKeeperStartupReconnectEvents>()
            .As<IBackgroundService>()
            .OrderBy(-1)
            .SingleInstance();
        builder.RegisterType<RestrictedJobInspector>().As<IRestrictedJobInspector>().SingleInstance();
        builder.RegisterType<JobKeeperJobVerifier>().As<IJobKeeperJobVerifier>().SingleInstance();
        builder.RegisterType<SessionJobKeeperIdentityStore>().As<IJobKeeperIdentityStore>().SingleInstance();
        builder.RegisterType<ProcessJobManager>().As<IProcessJobManager>().SingleInstance();
        builder.RegisterType<JobKeeperRegistry>().As<IJobKeeperRegistry>().SingleInstance();
        builder.RegisterType<KernelObjectMandatoryLabelService>().As<IKernelObjectMandatoryLabelService>().SingleInstance();
        builder.RegisterType<BackupIntentNativeFileSystem>().As<IBackupIntentNativeFileSystem>().SingleInstance();
        builder.RegisterType<BackupIntentManagedFileSystemProbe>().As<IBackupIntentManagedFileSystemProbe>().SingleInstance();
        builder.RegisterType<BackupIntentFileSystem>().As<IBackupIntentFileSystem>().SingleInstance();
        builder.RegisterType<JobKeeperPipeServerFactory>().As<IJobKeeperPipeServerFactory>().SingleInstance();
        builder.RegisterType<JobKeeperProcessTerminator>().As<IJobKeeperProcessTerminator>().SingleInstance();
        builder.RegisterType<JobKeeperProcessHandleOpener>().As<IJobKeeperProcessHandleOpener>().SingleInstance();
        builder.RegisterType<JobKeeperProcessDiscovery>()
            .WithParameter(
                "jobKeeperExePath",
                Path.Combine(AppContext.BaseDirectory, PathConstants.JobKeeperExeName))
            .As<IJobKeeperProcessDiscovery>()
            .SingleInstance();
        builder.RegisterType<JobKeeperClientProcessQuery>().As<IJobKeeperClientProcessQuery>().SingleInstance();
        builder.RegisterType<JobKeeperProcessVerifier>()
            .WithParameter(
                "jobKeeperExePath",
                Path.Combine(AppContext.BaseDirectory, PathConstants.JobKeeperExeName))
            .As<IJobKeeperProcessVerifier>()
            .SingleInstance();
        builder.RegisterType<JobKeeperLaunchIpcClient>().As<IJobKeeperLaunchIpcClient>().SingleInstance();
        builder.RegisterType<JobKeeperService>()
            .WithParameter("waitForConnectionTimeout", TimeSpan.FromSeconds(10))
            .As<IJobKeeperService>()
            .SingleInstance();
        builder.RegisterType<WindowsProcessSnapshotSource>().As<IProcessSnapshotSource>().SingleInstance();
        builder.RegisterType<ProcessListService>().As<IProcessListService>().SingleInstance();
        builder.RegisterType<ProcessTerminationService>().As<IProcessTerminationService>().SingleInstance();
        builder.RegisterType<IdleMonitorService>().As<IIdleMonitorService>().SingleInstance();
        builder.RegisterType<PreviousWindowTracker>().As<IPreviousWindowTracker>().SingleInstance();
        builder.RegisterType<InteractiveUserSidCache>().As<IInteractiveUserSidCache>().SingleInstance();
        builder.RegisterType<InteractiveUserDesktopProvider>().As<IInteractiveUserDesktopProvider>().SingleInstance();
        builder.RegisterType<InteractiveUserRefreshCoordinator>().AsSelf().SingleInstance();
        builder.RegisterType<FolderBrowserDialogAdapterFactory>().As<IFolderBrowserDialogAdapterFactory>().SingleInstance();
        builder.RegisterType<OpenFileDialogAdapterFactory>().As<IOpenFileDialogAdapterFactory>().SingleInstance();
        builder.RegisterType<SaveFileDialogAdapterFactory>().As<ISaveFileDialogAdapterFactory>().SingleInstance();
        builder.RegisterType<ClipboardPasteInterceptService>()
            .As<IRequiresInitialization>()
            .OrderBy(5)
            .InstancePerLifetimeScope();
    }
}
