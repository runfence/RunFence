using Autofac;
using RunFence.Ipc;

namespace RunFence.Startup.Modules;

public class IpcModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<WindowsIpcIdentityExtractor>()
            .As<IIpcIdentityExtractor>()
            .InstancePerDependency();

        builder.RegisterType<IpcConnectionProcessor>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<IpcServerService>()
            .As<IIpcServerService>()
            .SingleInstance();

        builder.RegisterType<IpcUiInvoker>()
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

        builder.RegisterType<IpcAssociationHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DirectoryValidator>()
            .As<IDirectoryValidator>()
            .SingleInstance();
    }
}