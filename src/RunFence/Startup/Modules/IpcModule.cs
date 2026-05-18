using Autofac;
using RunFence.Ipc;

namespace RunFence.Startup.Modules;

public class IpcModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<WindowsIpcIdentityExtractor>()
            .As<IIpcIdentityExtractor>()
            .SingleInstance();

        builder.RegisterType<IpcConnectionProcessor>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<CurrentProcessSidProvider>()
            .As<ICurrentProcessSidProvider>()
            .SingleInstance();

        builder.RegisterType<IpcPipeSecurityFactory>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<IpcServerService>()
            .As<IIpcServerService>()
            .SingleInstance();

        builder.RegisterType<IpcUiInvoker>()
            .As<IIpcUiInvoker>()
            .SingleInstance();

        builder.RegisterType<IpcConfigHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<IpcLifecycleHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<IpcMessageHandler>()
            .As<IIpcMessageHandler>()
            .SingleInstance();

        builder.RegisterType<IpcLaunchHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ShellFolderOpener>()
            .As<IShellFolderOpener>()
            .SingleInstance();

        builder.RegisterType<IpcOpenFolderHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AssociationAccessDeniedNotifier>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<IpcAssociationHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DirectoryValidator>()
            .As<IDirectoryValidator>()
            .SingleInstance();
    }
}
