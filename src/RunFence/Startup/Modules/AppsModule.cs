using Autofac;
using RunFence.Acl;
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
        builder.RegisterType<ShortcutComGateway>().As<IShortcutGateway>().SingleInstance();
        builder.RegisterType<ConfigShortcutProtectionStateStore>()
            .As<IShortcutProtectionStateStore>()
            .InstancePerLifetimeScope();
        builder.RegisterType<ShortcutProtectionOwnershipCalculator>().AsSelf().SingleInstance();
        builder.RegisterType<ShortcutManagedDenyAceEditor>().AsSelf().SingleInstance();
        builder.RegisterType<InternalShortcutAclPolicy>().AsSelf().SingleInstance();
        builder.RegisterType<InternalShortcutAclEditor>().AsSelf().SingleInstance();
        builder.RegisterType<ShortcutProtectionService>().As<IShortcutProtectionService>().InstancePerLifetimeScope();
        builder.RegisterType<BesideTargetShortcutService>().As<IBesideTargetShortcutService>().InstancePerLifetimeScope();
        builder.RegisterType<ManagedShortcutLifecycleService>().As<IManagedShortcutLifecycleService>().InstancePerLifetimeScope();
        builder.RegisterType<ShortcutTraversalScanner>().As<IShortcutTraversalScanner>().SingleInstance();
        builder.RegisterType<PowerShellAppxPackageQueryService>().As<IAppxPackageQueryService>().SingleInstance();
        builder.RegisterType<WindowsAppsAppDiscoveryService>().As<IWindowsAppsAppDiscoveryService>().SingleInstance();
        builder.RegisterType<ShortcutDiscoveryService>().As<IShortcutDiscoveryService>().SingleInstance();
        builder.RegisterType<ShortcutIconNativeApi>().As<IShortcutIconNativeApi>().SingleInstance();
        builder.RegisterType<ExecutableIconCountReader>().As<IExecutableIconCountReader>().SingleInstance();
        builder.RegisterType<ShortcutFinder>().AsSelf().SingleInstance();
        builder.RegisterType<ShortcutDestinationNativeApi>().As<IShortcutDestinationNativeApi>().SingleInstance();
        builder.RegisterType<ShortcutDestinationEntryAccessor>().As<IShortcutDestinationEntryAccessor>().SingleInstance();
        builder.RegisterType<ShortcutFilePersistenceNative>().As<IShortcutFilePersistenceNative>().SingleInstance();
        builder.RegisterType<ShortcutFilePersistenceService>()
            .WithParameter("trustedTempRootPath", Path.Combine(Path.GetTempPath(), "RunFence", "ShortcutPersistence"))
            .As<IShortcutFilePersistenceService>()
            .SingleInstance();
        builder.RegisterType<ShortcutWriteAccessService>().As<IShortcutWriteAccessService>().SingleInstance();
        builder.RegisterType<ShortcutService>().As<IShortcutService>().InstancePerLifetimeScope();
        builder.RegisterType<IconService>().As<IIconService>().SingleInstance();
        builder.RegisterType<HandlerAssociationsSection>().AsSelf().InstancePerDependency();
    }
}
