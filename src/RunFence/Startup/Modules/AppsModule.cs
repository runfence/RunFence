using Autofac;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Startup.Modules;

public class AppsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AppEntryIdGenerator>()
            .As<IAppEntryIdGenerator>()
            .SingleInstance();

        builder.RegisterType<InteractiveUserAssociationReader>().As<IInteractiveUserAssociationReader>().InstancePerDependency();
        builder.RegisterType<AssociationPolicyService>().As<IAssociationPolicyService>().SingleInstance();
        builder.RegisterType<ContextMenuService>().As<IContextMenuService>().SingleInstance();
        builder.RegisterType<ShortcutOperationRunner>().As<IShortcutOperationRunner>().SingleInstance();
        builder.RegisterType<AutoStartShortcutStore>().As<IAutoStartShortcutStore>().SingleInstance();
        builder.RegisterType<AutoStartService>().As<IAutoStartService>().SingleInstance();
        builder.RegisterType<ShortcutIconHelper>().As<IShortcutIconHelper>().SingleInstance();
        builder.RegisterType<ShortcutComHelper>().As<IShortcutComHelper>().SingleInstance();
        builder.RegisterType<ShortcutProtectionStateStore>()
            .WithParameter(
                "rootDirectory",
                Path.Combine(PathConstants.ProgramDataDir, "ShortcutProtectionState"))
            .As<IShortcutProtectionStateStore>()
            .SingleInstance();
        builder.RegisterType<ShortcutProtectionService>().As<IShortcutProtectionService>().SingleInstance();
        builder.RegisterType<BesideTargetShortcutService>().As<IBesideTargetShortcutService>().SingleInstance();
        builder.RegisterType<ShortcutTraversalScanner>().As<IShortcutTraversalScanner>().SingleInstance();
        builder.RegisterType<ShortcutDiscoveryService>().As<IShortcutDiscoveryService>().SingleInstance();
        builder.RegisterType<ShortcutFinder>().AsSelf().SingleInstance();
        builder.RegisterType<ShortcutFilePersistenceNative>().As<IShortcutFilePersistenceNative>().SingleInstance();
        builder.RegisterType<ShortcutFilePersistenceService>()
            .WithParameter("trustedTempRootPath", Path.Combine(Path.GetTempPath(), "RunFence", "ShortcutPersistence"))
            .As<IShortcutFilePersistenceService>()
            .SingleInstance();
        builder.RegisterType<ShortcutWriteAccessService>().As<IShortcutWriteAccessService>().SingleInstance();
        builder.RegisterType<ShortcutService>().As<IShortcutService>().SingleInstance();
        builder.RegisterType<IconService>().As<IIconService>().SingleInstance()
            .WithParameter(new NamedParameter("iconDir", PathConstants.ProgramDataIconDir));
        builder.RegisterType<HandlerAssociationsSection>().AsSelf().InstancePerDependency();
    }
}
