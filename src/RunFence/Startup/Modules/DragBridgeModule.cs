using Autofac;
using Autofac.Extras.Ordering;
using RunFence.DragBridge;
using RunFence.DragBridge.UI.Forms;
using RunFence.Infrastructure;
using RunFence.MediaBridge;

namespace RunFence.Startup.Modules;

public class DragBridgeModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<DragBridgeProcessLauncher>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<CapturedFileStore>()
            .As<ICapturedFileStore>()
            .SingleInstance();

        builder.RegisterType<DragBridgeCopyFlow>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DragBridgePasteHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DragBridgeService>()
            .As<IDragBridgeService>()
            .As<IRequiresInitialization>()
            .OrderBy(3)
            .SingleInstance();

        builder.RegisterType<GlobalHotkeyService>()
            .As<IGlobalHotkeyService>()
            .As<IRequiresInitialization>()
            .OrderBy(1)
            .SingleInstance();

        builder.RegisterType<CoreAudioSessionChecker>()
            .As<ICoreAudioSessionChecker>()
            .SingleInstance();

        builder.RegisterType<MediaKeyBridgeService>()
            .As<IMediaKeyBridgeService>()
            .As<IRequiresInitialization>()
            .OrderBy(2)
            .SingleInstance();

        builder.RegisterType<NotificationService>()
            .As<INotificationService>()
            .SingleInstance();

        builder.RegisterType<DragBridgeLauncher>()
            .As<IDragBridgeLauncher>()
            .SingleInstance();

        builder.RegisterType<WindowOwnerDetector>()
            .As<IWindowOwnerDetector>()
            .SingleInstance();

        builder.RegisterType<DragBridgeTempFileManager>()
            .As<IDragBridgeTempFileManager>()
            .SingleInstance();

        builder.RegisterType<DragBridgeAccessPrompt>()
            .As<IDragBridgeAccessPrompt>()
            .SingleInstance();
    }
}