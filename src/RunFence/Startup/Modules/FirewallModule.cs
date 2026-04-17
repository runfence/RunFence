using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Firewall.UI.Forms;
using RunFence.Firewall.Wfp;
using RunFence.Infrastructure;

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

        builder.RegisterType<FirewallResolvedDomainCache>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallEnforcementRetryState>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallComRuleApplier>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallWfpRuleApplier>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FirewallAccountRuleApplier>()
            .AsSelf()
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

        builder.RegisterType<FirewallDnsRefreshService>()
            .AsSelf()
            .As<IBackgroundService>()
            .As<IFirewallDomainRefreshRequester>()
            .OrderBy(2)
            .SingleInstance();

        builder.RegisterType<WfpEphemeralPortScanner>()
            .As<IBackgroundService>()
            .OrderBy(3)
            .SingleInstance();

        builder.RegisterType<EventLogBlockedConnectionReader>()
            .As<IBlockedConnectionReader>()
            .SingleInstance();

        builder.RegisterType<DefaultDnsResolver>()
            .As<IDnsResolver>()
            .SingleInstance();

        builder.RegisterType<FirewallApplyHelper>().AsSelf().SingleInstance();
        builder.RegisterType<FirewallAllowlistValidator>().AsSelf().SingleInstance();
        builder.RegisterType<FirewallPortValidator>().AsSelf().SingleInstance();
        builder.RegisterType<FirewallDomainResolver>().AsSelf().SingleInstance();
        builder.RegisterType<BlockedConnectionAggregator>().AsSelf().SingleInstance();
        builder.RegisterType<FirewallAllowlistImportExportService>().AsSelf().SingleInstance();
        builder.RegisterType<FirewallDialogFactory>().AsSelf().SingleInstance();
    }
}
