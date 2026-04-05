using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class CredentialFilterHelperTests
{
    private readonly Mock<ISidResolver> _resolver = new();

    private static CredentialEntry MakeCred(string sid) =>
        new() { Id = Guid.NewGuid(), Sid = sid, EncryptedPassword = [] };

    // --- Retention conditions ---

    [Fact]
    public void Filter_CurrentAccountCredential_AlwaysRetained()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var cred = MakeCred(currentSid);

        var result = CredentialFilterHelper.FilterResolvableCredentials(
            [cred], sidNames: null, _resolver.Object);

        Assert.Single(result);
        Assert.Equal(currentSid, result[0].Sid);
    }

    [Fact]
    public void Filter_ResolvableSid_RetainedViaResolver()
    {
        var sid = "S-1-5-21-1000000000-1000000000-1000000000-1001";
        var cred = MakeCred(sid);
        _resolver.Setup(r => r.TryResolveName(sid)).Returns(@"DOMAIN\user");

        var result = CredentialFilterHelper.FilterResolvableCredentials(
            [cred], sidNames: null, _resolver.Object);

        Assert.Single(result);
    }

    [Fact]
    public void Filter_SidInSidNames_Retained()
    {
        var sid = "S-1-5-21-1000000000-1000000000-1000000000-1002";
        var cred = MakeCred(sid);
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [sid] = "SomeUser" };
        _resolver.Setup(r => r.TryResolveName(sid)).Returns((string?)null);

        var result = CredentialFilterHelper.FilterResolvableCredentials(
            [cred], sidNames, _resolver.Object);

        Assert.Single(result);
    }

    [Fact]
    public void Filter_SidMatchingExistingApp_RetainedCaseInsensitive()
    {
        var sid = "S-1-5-21-1000000000-1000000000-1000000000-1003";
        var cred = MakeCred(sid.ToUpper());
        var existing = new AppEntry { AccountSid = sid.ToLower() };
        _resolver.Setup(r => r.TryResolveName(It.IsAny<string>())).Returns((string?)null);

        var result = CredentialFilterHelper.FilterResolvableCredentials(
            [cred], sidNames: null, _resolver.Object, existing);

        Assert.Single(result);
    }

    [Fact]
    public void Filter_UnresolvableSid_NotInSidNames_NoExistingMatch_Filtered()
    {
        var sid = "S-1-5-21-9999999999-9999999999-9999999999-9999";
        var cred = MakeCred(sid);
        _resolver.Setup(r => r.TryResolveName(sid)).Returns((string?)null);

        var result = CredentialFilterHelper.FilterResolvableCredentials(
            [cred], sidNames: null, _resolver.Object);

        Assert.Empty(result);
    }

    // --- Null parameter handling ---

    [Fact]
    public void Filter_NullSidNames_DoesNotThrow_FiltersUnresolvable()
    {
        var sid = "S-1-5-21-9999999999-9999999999-9999999999-8888";
        var cred = MakeCred(sid);
        _resolver.Setup(r => r.TryResolveName(sid)).Returns((string?)null);

        var result = CredentialFilterHelper.FilterResolvableCredentials(
            [cred], sidNames: null, _resolver.Object);

        Assert.Empty(result);
    }

    [Fact]
    public void Filter_NullExisting_DoesNotThrow_FiltersByOtherConditions()
    {
        var sid = "S-1-5-21-9999999999-9999999999-9999999999-7777";
        var cred = MakeCred(sid);
        _resolver.Setup(r => r.TryResolveName(sid)).Returns((string?)null);

        var result = CredentialFilterHelper.FilterResolvableCredentials(
            [cred], sidNames: null, _resolver.Object, existing: null);

        Assert.Empty(result);
    }

    [Fact]
    public void Filter_EmptyCredentials_ReturnsEmpty()
    {
        var result = CredentialFilterHelper.FilterResolvableCredentials(
            [], sidNames: null, _resolver.Object);

        Assert.Empty(result);
    }

    // --- Multiple credentials, order-independent filtering ---

    [Fact]
    public void Filter_MultipleCredentials_IndependentConditions()
    {
        var resolverSid = "S-1-5-21-1000000000-1000000000-1000000000-1010";
        var sidNameSid = "S-1-5-21-1000000000-1000000000-1000000000-1011";
        var filteredSid = "S-1-5-21-9999999999-9999999999-9999999999-6666";

        _resolver.Setup(r => r.TryResolveName(resolverSid)).Returns(@"DOMAIN\userA");
        _resolver.Setup(r => r.TryResolveName(sidNameSid)).Returns((string?)null);
        _resolver.Setup(r => r.TryResolveName(filteredSid)).Returns((string?)null);

        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { [sidNameSid] = "UserB" };

        var result = CredentialFilterHelper.FilterResolvableCredentials(
            [MakeCred(resolverSid), MakeCred(sidNameSid), MakeCred(filteredSid)],
            sidNames, _resolver.Object);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Sid == resolverSid);
        Assert.Contains(result, c => c.Sid == sidNameSid);
    }
}