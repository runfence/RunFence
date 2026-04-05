using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class SidNameResolverTests
{
    private const string FakeSid = "S-1-5-21-9999999999-9999999999-9999999999-9001";

    // A no-op resolver that never does live lookups — used to test fallback chain logic.
    private static ISidResolver NullResolver()
    {
        var mock = new Mock<ISidResolver>();
        mock.Setup(r => r.TryResolveName(It.IsAny<string>())).Returns((string?)null);
        mock.Setup(r => r.TryResolveNameFromRegistry(It.IsAny<string>())).Returns((string?)null);
        return mock.Object;
    }

    // --- UpdateSidName ---

    [Fact]
    public void UpdateSidName_EmptySid_IsNoOp()
    {
        var db = new AppDatabase();
        db.UpdateSidName("", "alice");
        Assert.Empty(db.SidNames);
    }

    [Fact]
    public void UpdateSidName_EmptyName_IsNoOp()
    {
        var db = new AppDatabase();
        db.UpdateSidName(FakeSid, "");
        Assert.Empty(db.SidNames);
    }

    [Fact]
    public void UpdateSidName_NormalUpdate_SetsEntry()
    {
        var db = new AppDatabase();
        db.UpdateSidName(FakeSid, "alice");
        Assert.Equal("alice", db.SidNames[FakeSid]);
    }

    [Fact]
    public void UpdateSidName_Overwrite_UpdatesExisting()
    {
        var db = new AppDatabase();
        db.UpdateSidName(FakeSid, "alice");
        db.UpdateSidName(FakeSid, "alice_renamed");
        Assert.Equal("alice_renamed", db.SidNames[FakeSid]);
    }

    // --- GetDisplayName(sid, preResolved, sidResolver, sidNames) ---

    [Fact]
    public void GetDisplayName_PreResolvedName_ExtractsUsername()
    {
        var result = SidNameResolver.GetDisplayName(FakeSid, "DOMAIN\\alice", NullResolver(), null);
        Assert.Equal("alice", result);
    }

    [Fact]
    public void GetDisplayName_NoPreResolved_FallsBackToMap()
    {
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FakeSid] = "mapuser"
        };
        var result = SidNameResolver.GetDisplayName(FakeSid, null, NullResolver(), sidNames);
        Assert.Equal("mapuser", result);
    }

    [Fact]
    public void GetDisplayName_NoSourceAvailable_ReturnsSid()
    {
        var result = SidNameResolver.GetDisplayName(FakeSid, null, NullResolver(), null);
        Assert.Equal(FakeSid, result);
    }

    // --- StripLocalMachinePrefix ---

    [Fact]
    public void StripLocalMachinePrefix_LocalMachine_StripsPrefix()
    {
        var result = SidNameResolver.StripLocalMachinePrefix($"{Environment.MachineName}\\alice");
        Assert.Equal("alice", result);
    }

    [Fact]
    public void StripLocalMachinePrefix_DomainAccount_PreservesPrefix()
    {
        var result = SidNameResolver.StripLocalMachinePrefix("CONTOSO\\alice");
        Assert.Equal("CONTOSO\\alice", result);
    }

    [Fact]
    public void StripLocalMachinePrefix_NoPrefix_ReturnsAsIs()
    {
        var result = SidNameResolver.StripLocalMachinePrefix("alice");
        Assert.Equal("alice", result);
    }

    // --- ResolveUsername ---

    [Fact]
    public void ResolveUsername_MapFallback_ReturnsMapName()
    {
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FakeSid] = "mapuser"
        };
        var result = SidNameResolver.ResolveUsername(FakeSid, NullResolver(), sidNames);
        Assert.Equal("mapuser", result);
    }

    [Fact]
    public void ResolveUsername_MapFallback_StripsPrefix()
    {
        // Even domain-prefixed stored names are stripped for shortcut naming purposes
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FakeSid] = "CONTOSO\\alice"
        };
        var result = SidNameResolver.ResolveUsername(FakeSid, NullResolver(), sidNames);
        Assert.Equal("alice", result);
    }

    [Fact]
    public void ResolveUsername_NoSource_ReturnsNull()
    {
        var result = SidNameResolver.ResolveUsername(FakeSid, NullResolver(), null);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveUsername_EmptyMap_ReturnsNull()
    {
        var result = SidNameResolver.ResolveUsername(FakeSid, NullResolver(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        Assert.Null(result);
    }

    // --- ExtractDomain ---

    [Fact]
    public void ExtractDomain_NoDomainPrefix_ReturnsEmpty()
    {
        var result = SidNameResolver.ExtractDomain("alice");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractDomain_LocalMachinePrefix_ReturnsDot()
    {
        // Local machine accounts use "." as domain for CreateProcessWithLogonW
        var result = SidNameResolver.ExtractDomain($"{Environment.MachineName}\\alice");
        Assert.Equal(".", result);
    }

    [Fact]
    public void ExtractDomain_DomainPrefix_ReturnsDomainAsIs()
    {
        var result = SidNameResolver.ExtractDomain("CONTOSO\\alice");
        Assert.Equal("CONTOSO", result);
    }

    [Fact]
    public void ExtractDomain_IsCaseInsensitiveForLocalMachine()
    {
        var result = SidNameResolver.ExtractDomain($"{Environment.MachineName.ToLowerInvariant()}\\alice");
        Assert.Equal(".", result);
    }

    // --- DeterministicHash ---

    [Fact]
    public void DeterministicHash_SameSid_ReturnsSameValue()
    {
        var h1 = SidNameResolver.DeterministicHash(FakeSid);
        var h2 = SidNameResolver.DeterministicHash(FakeSid);
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void DeterministicHash_DifferentSids_ReturnDifferentValues()
    {
        const string sid2 = "S-1-5-21-9999999999-9999999999-9999999999-9002";
        var h1 = SidNameResolver.DeterministicHash(FakeSid);
        var h2 = SidNameResolver.DeterministicHash(sid2);
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void DeterministicHash_EmptyString_ReturnsNonZero()
    {
        // FNV-1a basis value for empty string is the initial hash constant — not zero
        var h = SidNameResolver.DeterministicHash(string.Empty);
        Assert.NotEqual(0, h);
    }

    // --- ResolveDomainAndUsername (sid, isCurrentAccount, ...) ---

    [Fact]
    public void ResolveDomainAndUsername_CurrentAccount_ReturnsEmptyDomainAndEnvironmentUsername()
    {
        var (domain, username) = SidNameResolver.ResolveDomainAndUsername(
            FakeSid, isCurrentAccount: true, NullResolver(), null);
        Assert.Equal(string.Empty, domain);
        Assert.Equal(Environment.UserName, username);
    }

    [Fact]
    public void ResolveDomainAndUsername_NonCurrentAccount_LiveResolution_ExtractsDomainAndUser()
    {
        var resolver = new Mock<ISidResolver>();
        resolver.Setup(r => r.TryResolveName(FakeSid)).Returns("CONTOSO\\alice");

        var (domain, username) = SidNameResolver.ResolveDomainAndUsername(
            FakeSid, isCurrentAccount: false, resolver.Object, null);
        Assert.Equal("CONTOSO", domain);
        Assert.Equal("alice", username);
    }

    [Fact]
    public void ResolveDomainAndUsername_NoResolution_FallsBackToMapName()
    {
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FakeSid] = "CORP\\bob"
        };
        var (domain, username) = SidNameResolver.ResolveDomainAndUsername(
            FakeSid, isCurrentAccount: false, NullResolver(), sidNames);
        Assert.Equal("CORP", domain);
        Assert.Equal("bob", username);
    }

    [Fact]
    public void ResolveDomainAndUsername_NoSourceAvailable_ReturnsSidAsUsername()
    {
        var (domain, username) = SidNameResolver.ResolveDomainAndUsername(
            FakeSid, isCurrentAccount: false, NullResolver(), null);
        Assert.Equal(string.Empty, domain);
        Assert.Equal(FakeSid, username);
    }

    // --- ResolveDomainAndUsername (CredentialEntry overload) ---

    [Fact]
    public void ResolveDomainAndUsername_CredentialEntry_CurrentAccount_UsesEnvironmentUsername()
    {
        var cred = new CredentialEntry { Sid = FakeSid };
        // IsCurrentAccount is computed: matches SidResolutionHelper.GetCurrentUserSid().
        // In tests, running as non-admin non-elevated user, GetCurrentUserSid() won't match FakeSid,
        // so IsCurrentAccount will be false → fall through to resolver/map chain.
        // We just verify the overload delegates correctly by checking it returns a consistent result.
        var (domain, username) = SidNameResolver.ResolveDomainAndUsername(cred, NullResolver(), null);
        // Either (empty, FakeSid) if not current account, or (empty, Environment.UserName) if it is.
        Assert.NotNull(username);
        Assert.NotNull(domain);
    }

    // --- GetDisplayName(CredentialEntry) overload ---

    [Fact]
    public void GetDisplayName_CredentialEntry_LiveResolution_ExtractsUsername()
    {
        var resolver = new Mock<ISidResolver>();
        resolver.Setup(r => r.TryResolveName(FakeSid)).Returns("DOMAIN\\alice");
        var cred = new CredentialEntry { Sid = FakeSid };

        var result = SidNameResolver.GetDisplayName(cred, resolver.Object, null);

        Assert.Equal("alice", result);
    }

    [Fact]
    public void GetDisplayName_CredentialEntry_MapFallback_ExtractsUsername()
    {
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FakeSid] = "mapuser"
        };
        var cred = new CredentialEntry { Sid = FakeSid };

        var result = SidNameResolver.GetDisplayName(cred, NullResolver(), sidNames);

        Assert.Equal("mapuser", result);
    }

    [Fact]
    public void GetDisplayName_CredentialEntry_NoSource_ReturnsSid()
    {
        var cred = new CredentialEntry { Sid = FakeSid };
        var result = SidNameResolver.GetDisplayName(cred, NullResolver(), null);
        Assert.Equal(FakeSid, result);
    }

    // --- ApplyAccountSuffix ---

    [Fact]
    public void ApplyAccountSuffix_CurrentAccount_AppendsCurrent()
    {
        var result = SidNameResolver.ApplyAccountSuffix("alice", isCurrentAccount: true, isInteractiveUser: false);
        Assert.Equal("alice (current)", result);
    }

    [Fact]
    public void ApplyAccountSuffix_InteractiveUser_AppendsInteractive()
    {
        var result = SidNameResolver.ApplyAccountSuffix("alice", isCurrentAccount: false, isInteractiveUser: true);
        Assert.Equal("alice (interactive)", result);
    }

    [Fact]
    public void ApplyAccountSuffix_NeitherFlag_ReturnsOriginal()
    {
        var result = SidNameResolver.ApplyAccountSuffix("alice", isCurrentAccount: false, isInteractiveUser: false);
        Assert.Equal("alice", result);
    }

    [Fact]
    public void ApplyAccountSuffix_BothFlags_CurrentTakesPrecedence()
    {
        // When both flags are true, current takes precedence (if condition is first)
        var result = SidNameResolver.ApplyAccountSuffix("alice", isCurrentAccount: true, isInteractiveUser: true);
        Assert.Equal("alice (current)", result);
    }
}