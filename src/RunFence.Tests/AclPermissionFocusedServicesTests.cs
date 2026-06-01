using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AclPermissionFocusedServicesTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string TestDirectory = @"C:\Apps";

    [Fact]
    public void GrantableAncestorPolicy_NeedsPermissionGrant_ReturnsFalseWhenRightsAlreadyEffective()
    {
        var security = new FileSecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            FileSystemRights.ReadAndExecute,
            AccessControlType.Allow));
        var accessor = new Mock<IPathSecurityDescriptorAccessor>();
        accessor.Setup(mock => mock.GetSecurity(@"C:\Apps\App.exe")).Returns(security);
        var evaluator = new DeterministicAclAccessEvaluator();
        var resolver = new AclGroupSidResolver(
            new NTTranslateApi(Mock.Of<ILoggingService>()),
            new GroupMembershipApi(Mock.Of<ILoggingService>()),
            Mock.Of<ILocalGroupQueryService>());
        var policy = new GrantableAncestorPolicy(resolver, accessor.Object, evaluator);

        var needsGrant = policy.NeedsPermissionGrant(UserSid, @"C:\Apps\App.exe", FileSystemRights.ReadAndExecute);

        Assert.False(needsGrant);
    }

    [Fact]
    public void GrantableAncestorPolicy_GetGrantableAncestors_StopsBeforeBlockedRoot()
    {
        var resolver = new AclGroupSidResolver(
            new NTTranslateApi(Mock.Of<ILoggingService>()),
            new GroupMembershipApi(Mock.Of<ILoggingService>()),
            Mock.Of<ILocalGroupQueryService>());
        var policy = new GrantableAncestorPolicy(
            resolver,
            Mock.Of<IPathSecurityDescriptorAccessor>(),
            new DeterministicAclAccessEvaluator());

        var ancestors = policy.GetGrantableAncestors(UserSid, Path.Combine(Environment.SystemDirectory, "kernel32.dll"), FileSystemRights.ReadAndExecute);

        Assert.Empty(ancestors);
    }

    [Fact]
    public void TraverseRightsHelper_HasEffectiveTraverseForGrantSid_IgnoresAdministratorsGroupForAccountEvaluation()
    {
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(AclComputeHelper.AdministratorsSid.Value),
            TraverseRightsHelper.TraverseRights,
            AccessControlType.Allow));
        var pathInfo = new TestFileSystemPathInfo().AddDirectory(TestDirectory, security);
        var evaluator = new DeterministicAclAccessEvaluator();
        var resolver = new AclGroupSidResolver(
            new NTTranslateApi(Mock.Of<ILoggingService>()),
            new GroupMembershipApi(Mock.Of<ILoggingService>()),
            Mock.Of<ILocalGroupQueryService>());
        var permissionService = new AclPermissionService(
            resolver,
            new GrantableAncestorPolicy(
                resolver,
                Mock.Of<IPathSecurityDescriptorAccessor>(),
                evaluator),
            new AdminRestrictionAclWriter(Mock.Of<IPathSecurityDescriptorAccessor>()),
            evaluator);

        var hasTraverse = TraverseRightsHelper.HasEffectiveTraverseForGrantSid(
            TestDirectory,
            UserSid,
            ["S-1-1-0", "S-1-5-11", AclComputeHelper.AdministratorsSid.Value],
            permissionService,
            pathInfo);

        Assert.False(hasTraverse);
    }

    [Fact]
    public void TraverseRightsHelper_HasEffectiveTraverseForGrantSid_WhenNotUnelevated_KeepsAdministratorsGroup()
    {
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(AclComputeHelper.AdministratorsSid.Value),
            TraverseRightsHelper.TraverseRights,
            AccessControlType.Allow));
        var pathInfo = new TestFileSystemPathInfo().AddDirectory(TestDirectory, security);
        var evaluator = new DeterministicAclAccessEvaluator();
        var resolver = new AclGroupSidResolver(
            new NTTranslateApi(Mock.Of<ILoggingService>()),
            new GroupMembershipApi(Mock.Of<ILoggingService>()),
            Mock.Of<ILocalGroupQueryService>());
        var permissionService = new AclPermissionService(
            resolver,
            new GrantableAncestorPolicy(
                resolver,
                Mock.Of<IPathSecurityDescriptorAccessor>(),
                evaluator),
            new AdminRestrictionAclWriter(Mock.Of<IPathSecurityDescriptorAccessor>()),
            evaluator);

        var hasTraverse = TraverseRightsHelper.HasEffectiveTraverseForGrantSid(
            TestDirectory,
            UserSid,
            ["S-1-1-0", "S-1-5-11", AclComputeHelper.AdministratorsSid.Value],
            permissionService,
            pathInfo,
            unelevated: false);

        Assert.True(hasTraverse);
    }
}
