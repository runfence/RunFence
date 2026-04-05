using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Firewall;
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

        builder.RegisterType<FirewallService>()
            .As<IFirewallService>()
            .SingleInstance();

        builder.RegisterType<WfpLocalhostBlocker>()
            .As<IWfpLocalhostBlocker>()
            .SingleInstance();

        builder.RegisterType<FirewallDnsRefreshService>()
            .AsSelf()
            .As<IBackgroundService>()
            .OrderBy(2)
            .SingleInstance();

        builder.RegisterType<EventLogBlockedConnectionReader>()
            .As<IBlockedConnectionReader>()
            .SingleInstance();

        builder.RegisterType<DefaultDnsResolver>()
            .As<IDnsResolver>()
            .SingleInstance();
    }
}