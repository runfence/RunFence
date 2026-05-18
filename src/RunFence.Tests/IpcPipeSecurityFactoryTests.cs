using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Ipc;
using Xunit;

namespace RunFence.Tests;

public class IpcPipeSecurityFactoryTests
{
    [Fact]
    public void ServerAccountCanCreateNextPipeInstance()
    {
        string pipeName = $"RunFence_Test_{Guid.NewGuid():N}";
        var securityFactory = new IpcPipeSecurityFactory(new CurrentProcessSidProvider());

        using var firstPipe = CreatePipe(
            pipeName,
            securityFactory.Create(),
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance);
        using var secondPipe = CreatePipe(
            pipeName,
            securityFactory.Create(),
            PipeOptions.Asynchronous);

        Assert.NotNull(firstPipe);
        Assert.NotNull(secondPipe);
    }

    [Fact]
    public void CallerRulesAllowRestrictedTokensWithoutCreateInstanceRights()
    {
        var securityFactory = new IpcPipeSecurityFactory(new CurrentProcessSidProvider());

        var rules = securityFactory.Create()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .OfType<PipeAccessRule>()
            .ToList();

        AssertCallerRule(rules, WellKnownSidType.WorldSid);
        AssertCallerRule(rules, WellKnownSidType.RestrictedCodeSid);
    }

    [Fact]
    public void NetworkSid_IsExplicitlyDeniedFullControl()
    {
        var securityFactory = new IpcPipeSecurityFactory(new CurrentProcessSidProvider());
        var rules = securityFactory.Create()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .OfType<PipeAccessRule>()
            .ToList();

        var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        var denyRule = Assert.Single(rules, r =>
            r.IdentityReference.Equals(networkSid) && r.AccessControlType == AccessControlType.Deny);
        Assert.True(denyRule.PipeAccessRights.HasFlag(PipeAccessRights.FullControl));
    }

    private static void AssertCallerRule(List<PipeAccessRule> rules, WellKnownSidType sidType)
    {
        var sid = new SecurityIdentifier(sidType, null);
        var rule = Assert.Single(rules, r =>
            r.IdentityReference.Equals(sid) && r.AccessControlType == AccessControlType.Allow);

        Assert.True(rule.PipeAccessRights.HasFlag(PipeAccessRights.ReadWrite));
        Assert.True(rule.PipeAccessRights.HasFlag(PipeAccessRights.Synchronize));
        Assert.False(rule.PipeAccessRights.HasFlag(PipeAccessRights.CreateNewInstance));
    }

    private NamedPipeServerStream CreatePipe(string pipeName, PipeSecurity pipeSecurity, PipeOptions options) =>
        NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            8,
            PipeTransmissionMode.Message,
            options,
            IpcConstants.MaxPipeMessageSize,
            IpcConstants.MaxPipeMessageSize,
            pipeSecurity);
}
