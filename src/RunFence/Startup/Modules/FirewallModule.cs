using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Core;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Firewall.UI.Forms;
using RunFence.Firewall.Wfp;
using RunFence.Infrastructure;
using RunFence.Startup.NonElevatedMocks;

namespace RunFence.Startup.Modules;

public class FirewallModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ComFirewallRuleManager>()
            .As<IFirewallRuleManager>()
            .SingleInstance();

        builder.RegisterType<DefaultNetworkInterfaceInfoProvider>()
            .As<INetworkInterfaceInfoProvider>()
            .SingleInstance();

        builder.RegisterType<FirewallNetworkInfoService>()
            .As<IFirewallNetworkInfo>()
            .SingleInstance();

        builder.RegisterType<FirewallAddressRangeBuilder>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallAddressExclusionBuilder>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallDomainBatchResolver>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallDomainDirtyTracker>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallResolvedDomainCache>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallEnforcementRetryState>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallDnsRefreshCycleRunner>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallEnforcementRetryProcessor>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallApplyPlanner>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallApplyRetryCoordinator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallApplyPhaseExecutor>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallComRuleApplier>()
            .As<IFirewallComRuleApplier>()
            .SingleInstance();

        builder.RegisterType<FirewallWfpRuleApplier>()
            .As<IFirewallWfpRuleApplier>()
            .SingleInstance();

        builder.RegisterType<FirewallAccountRuleApplier>()
            .As<IFirewallAccountRuleApplier>()
            .As<IFirewallDnsRefreshTarget>()
            .SingleInstance();

        builder.RegisterType<FirewallGlobalIcmpEnforcer>()
            .AsSelf()
            .As<IGlobalIcmpPolicyService>()
            .SingleInstance();

        builder.RegisterType<FirewallRuleCleanupService>()
            .As<IFirewallCleanupService>()
            .SingleInstance();

        builder.RegisterType<GlobalIcmpEnforcementOrchestrator>()
            .As<IGlobalIcmpSettingsApplier>()
            .As<IGlobalIcmpPendingDomainProcessor>()
            .As<IGlobalIcmpEnforcementTrigger>()
            .SingleInstance();

        builder.RegisterType<FirewallEnforcementOrchestrator>()
            .As<IAccountFirewallSettingsApplier>()
            .As<IFirewallEnforcementOrchestrator>()
            .SingleInstance();

        builder.RegisterType<WfpFilterHelperService>()
            .As<IWfpFilterHelper>()
            .SingleInstance();

        builder.RegisterType<WfpLocalhostBlocker>()
            .As<IWfpLocalhostBlocker>()
            .SingleInstance();

        builder.RegisterType<WfpIcmpBlocker>()
            .As<IWfpIcmpBlocker>()
            .SingleInstance();

        builder.RegisterType<WfpGlobalIcmpBlocker>()
            .As<IWfpGlobalIcmpBlocker>()
            .SingleInstance();

        builder.RegisterType<EphemeralPortOwnershipSnapshotProvider>()
            .As<IEphemeralPortOwnershipSnapshotProvider>()
            .SingleInstance();

        builder.RegisterType<EphemeralPortSnapshotReader>()
            .As<IEphemeralPortSnapshotReader>()
            .SingleInstance();

        builder.RegisterType<FirewallDnsRefreshService>()
            .AsSelf()
            .As<IBackgroundService>()
            .As<IFirewallDomainRefreshRequester>()
            .OrderBy(2)
            .SingleInstance();

        builder.RegisterType<WfpEphemeralPortScanner>()
            .WithParameter("startTimer", true)
            .As<IBackgroundService>()
            .OrderBy(3)
            .SingleInstance();

        builder.RegisterType<EventLogBlockedConnectionReader>()
            .As<IBlockedConnectionReader>()
            .As<IAuditPolicyService>()
            .SingleInstance();

        builder.RegisterType<SecurityEventLogBlockedConnectionEventSource>()
            .As<IBlockedConnectionEventSource>()
            .SingleInstance();

        builder.RegisterType<WindowsEventLogRecordSource>()
            .As<IEventLogRecordSource>()
            .SingleInstance();

        builder.RegisterType<AuditPolCommandRunner>()
            .As<IAuditPolCommandRunner>()
            .SingleInstance();

        builder.RegisterType<DefaultDnsResolver>()
            .As<IDnsResolver>()
            .SingleInstance();

        builder.RegisterType<NetshCommandRunner>().As<INetshCommandRunner>().SingleInstance();
        builder.RegisterType<DynamicPortRangeChecker>().AsSelf().SingleInstance();
        builder.RegisterType<FirewallApplyHelper>()
            .As<IFirewallApplyHelper>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<FirewallAllowlistValidator>().AsSelf().SingleInstance();
        builder.RegisterType<FirewallPortValidator>().AsSelf().SingleInstance();
        builder.RegisterType<FirewallDomainResolver>().AsSelf().SingleInstance();
        builder.RegisterType<BlockedConnectionAggregator>().AsSelf().SingleInstance();
        builder.RegisterType<FirewallAllowlistImportExportService>().AsSelf().SingleInstance();
        builder.RegisterType<BlockedConnectionsFlowHelper>().AsSelf().SingleInstance();
        builder.RegisterType<FirewallDialogApplyPresenter>().AsSelf().SingleInstance();
        builder.RegisterType<FirewallDialogFactory>()
            .As<IFirewallDialogFactory>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallSettingsService>()
            .As<IFirewallSettingsService>()
            .SingleInstance();

        builder.RegisterType<AccountFirewallToggleService>()
            .As<IAccountFirewallToggle>()
            .SingleInstance();

        if (DebugHelper.UseAdminOperationMocks)
        {
            builder.RegisterDecorator<NoOpFirewallRuleManager, IFirewallRuleManager>();
            builder.RegisterDecorator<NoOpWfpLocalhostBlocker, IWfpLocalhostBlocker>();
            builder.RegisterDecorator<NoOpWfpIcmpBlocker, IWfpIcmpBlocker>();
            builder.RegisterDecorator<NoOpWfpGlobalIcmpBlocker, IWfpGlobalIcmpBlocker>();
            builder.RegisterDecorator<NoOpBlockedConnectionReader, IBlockedConnectionReader>();
            builder.RegisterDecorator<NoOpAuditPolicyService, IAuditPolicyService>();
        }
    }
}
