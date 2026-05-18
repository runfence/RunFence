using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Moq;
using RunFence.Core;
using RunFence.Launch.Container;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AppContainerComAccessServiceTests : IDisposable
{
    private readonly RegistryTestHelper _registry = new("AppContainerComHku", "AppContainerComHklm");
    private readonly string _containerSid = new AppContainerSidProvider().GetSidString("ram_test_container");
    private readonly string _systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;

    public void Dispose()
    {
        _registry.Dispose();
    }

    [Fact]
    public void GrantComAccess_PreservesExistingDescriptors()
    {
        const string clsid = "{11111111-1111-1111-1111-111111111111}";
        using var appIdKey = _registry.HkuRoot.CreateSubKey($@"AppID\{clsid}")!;
        var existing = CreateDescriptor((_systemSid, 11));
        appIdKey.SetValue("LaunchPermission", existing, RegistryValueKind.Binary);
        appIdKey.SetValue("AccessPermission", existing, RegistryValueKind.Binary);

        var result = CreateService().GrantComAccess(_containerSid, clsid);

        Assert.True(result.Succeeded);
        AssertContainsAce(appIdKey, "LaunchPermission", _systemSid, 11);
        AssertContainsAce(appIdKey, "LaunchPermission", _containerSid, 11);
        AssertContainsAce(appIdKey, "AccessPermission", _systemSid, 11);
        AssertContainsAce(appIdKey, "AccessPermission", _containerSid, 11);
    }

    [Fact]
    public void GrantComAccess_UsesMachineDefaultsWhenPerAppValuesMissing()
    {
        const string clsid = "{22222222-2222-2222-2222-222222222222}";
        using var oleKey = _registry.HklmRoot.CreateSubKey(@"SOFTWARE\Microsoft\Ole")!;
        oleKey.SetValue("DefaultLaunchPermission", CreateDescriptor((_systemSid, 11)), RegistryValueKind.Binary);
        oleKey.SetValue("DefaultAccessPermission", CreateDescriptor((_systemSid, 11)), RegistryValueKind.Binary);

        var result = CreateService().GrantComAccess(_containerSid, clsid);

        using var appIdKey = _registry.HkuRoot.OpenSubKey($@"AppID\{clsid}")!;
        Assert.True(result.Succeeded);
        AssertContainsAce(appIdKey, "LaunchPermission", _systemSid, 11);
        AssertContainsAce(appIdKey, "LaunchPermission", _containerSid, 11);
        AssertContainsAce(appIdKey, "AccessPermission", _systemSid, 11);
        AssertContainsAce(appIdKey, "AccessPermission", _containerSid, 11);
    }

    [Fact]
    public void RevokeComAccess_RemovesOnlyContainerAceAndKeepsValues()
    {
        const string clsid = "{33333333-3333-3333-3333-333333333333}";
        using var appIdKey = _registry.HkuRoot.CreateSubKey($@"AppID\{clsid}")!;
        var descriptor = CreateDescriptor((_systemSid, 11), (_containerSid, 11));
        appIdKey.SetValue("LaunchPermission", descriptor, RegistryValueKind.Binary);
        appIdKey.SetValue("AccessPermission", descriptor, RegistryValueKind.Binary);

        var result = CreateService().RevokeComAccess(_containerSid, clsid);

        Assert.True(result.Succeeded);
        Assert.NotNull(appIdKey.GetValue("LaunchPermission"));
        Assert.NotNull(appIdKey.GetValue("AccessPermission"));
        AssertContainsAce(appIdKey, "LaunchPermission", _systemSid, 11);
        AssertDoesNotContainAce(appIdKey, "LaunchPermission", _containerSid);
        AssertContainsAce(appIdKey, "AccessPermission", _systemSid, 11);
        AssertDoesNotContainAce(appIdKey, "AccessPermission", _containerSid);
    }

    [Fact]
    public void RevokeComAccess_WhenAppIdKeyDoesNotExist_DoesNotCreateIt()
    {
        const string clsid = "{66666666-6666-6666-6666-666666666666}";

        var result = CreateService().RevokeComAccess(_containerSid, clsid);

        Assert.True(result.Succeeded);
        Assert.Null(_registry.HkuRoot.OpenSubKey($@"AppID\{clsid}"));
    }

    [Fact]
    public void GrantComAccess_WhenSecondPermissionFails_RollsBackCreatedValues()
    {
        const string clsid = "{44444444-4444-4444-4444-444444444444}";
        using var oleKey = _registry.HklmRoot.CreateSubKey(@"SOFTWARE\Microsoft\Ole")!;
        oleKey.SetValue("DefaultLaunchPermission", CreateDescriptor((_systemSid, 11)), RegistryValueKind.Binary);
        oleKey.SetValue("DefaultAccessPermission", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);

        var result = CreateService().GrantComAccess(_containerSid, clsid);

        using var appIdKey = _registry.HkuRoot.OpenSubKey($@"AppID\{clsid}")!;
        Assert.False(result.Succeeded);
        Assert.Null(appIdKey.GetValue("LaunchPermission"));
        Assert.Null(appIdKey.GetValue("AccessPermission"));
    }

    [Fact]
    public void GrantComAccess_WhenAppIdRootIsReadOnly_ReturnsFailure()
    {
        const string clsid = "{55555555-5555-5555-5555-555555555555}";
        using var oleKey = _registry.HklmRoot.CreateSubKey(@"SOFTWARE\Microsoft\Ole")!;
        oleKey.SetValue("DefaultLaunchPermission", CreateDescriptor((_systemSid, 11)), RegistryValueKind.Binary);
        oleKey.SetValue("DefaultAccessPermission", CreateDescriptor((_systemSid, 11)), RegistryValueKind.Binary);

        var readOnlyPath = _registry.HkuRoot.Name["HKEY_CURRENT_USER\\".Length..];
        using var readOnlyRoot = Registry.CurrentUser.OpenSubKey(readOnlyPath, writable: false)!;

        var result = CreateService(appIdRootOverride: readOnlyRoot).GrantComAccess(_containerSid, clsid);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    private AppContainerComAccessService CreateService(
        RegistryKey? appIdRootOverride = null,
        RegistryKey? machineRootOverride = null)
        => new(
            new Mock<ILoggingService>().Object,
            AppContainerProviderTestDoubles.CreateComRegistryRoots(
                appIdRootOverride ?? _registry.HkuRoot,
                machineRootOverride ?? _registry.HklmRoot));

    private static byte[] CreateDescriptor(params (string Sid, int AccessMask)[] aces)
    {
        var dacl = new RawAcl(GenericAcl.AclRevision, aces.Length);
        foreach (var ace in aces)
        {
            dacl.InsertAce(dacl.Count, new CommonAce(
                AceFlags.None,
                AceQualifier.AccessAllowed,
                ace.AccessMask,
                new SecurityIdentifier(ace.Sid),
                isCallback: false,
                opaque: null));
        }

        var descriptor = new RawSecurityDescriptor(
            ControlFlags.DiscretionaryAclPresent | ControlFlags.SelfRelative,
            owner: null,
            group: null,
            systemAcl: null,
            discretionaryAcl: dacl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private static void AssertContainsAce(RegistryKey appIdKey, string valueName, string sid, int accessMask)
    {
        var descriptor = new RawSecurityDescriptor((byte[])appIdKey.GetValue(valueName)!, 0);
        Assert.Contains(
            descriptor.DiscretionaryAcl!.Cast<GenericAce>().OfType<CommonAce>(),
            ace => ace.SecurityIdentifier.Value == sid && ace.AccessMask == accessMask);
    }

    private static void AssertDoesNotContainAce(RegistryKey appIdKey, string valueName, string sid)
    {
        var descriptor = new RawSecurityDescriptor((byte[])appIdKey.GetValue(valueName)!, 0);
        Assert.DoesNotContain(
            descriptor.DiscretionaryAcl!.Cast<GenericAce>().OfType<CommonAce>(),
            ace => ace.SecurityIdentifier.Value == sid);
    }
}
