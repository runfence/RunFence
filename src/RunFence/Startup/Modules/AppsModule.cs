using Autofac;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Startup.Modules;

public class AppsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AppEntryEnforcementHelper>().AsSelf().SingleInstance();
        builder.RegisterType<ContextMenuService>().As<IContextMenuService>().SingleInstance();
        builder.RegisterType<AutoStartService>().As<IAutoStartService>().SingleInstance();
        builder.RegisterType<ShortcutProtectionService>().As<IShortcutProtectionService>().SingleInstance();
        builder.RegisterType<BesideTargetShortcutService>().As<IBesideTargetShortcutService>().SingleInstance();
        builder.RegisterType<ShortcutDiscoveryService>().As<IShortcutDiscoveryService>().SingleInstance();
        builder.RegisterType<ShortcutService>().As<IShortcutService>().SingleInstance();
        builder.RegisterType<IconService>().As<IIconService>().SingleInstance();
    }
}