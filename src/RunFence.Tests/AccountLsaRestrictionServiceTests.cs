using System.Security.Principal;
using Moq;
using RunFence.Account;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class AccountLsaRestrictionServiceTests : IDisposable
{
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAccountValidationService> _accountValidation = new();
    private readonly AccountLoginRestrictionService _loginService;
    private readonly AccountLsaRestrictionService _lsaService;
    private readonly TempDirectory _tempDir;

    public AccountLsaRestrictionServiceTests()
    {
        _tempDir = new TempDirectory("RunFence_LsaTest");
        var lsa = new Mock<ILsaRightsHelper>();
        lsa.Setup(x => x.GetSidBytes(It.IsAny<string>())).Returns((string s) =>
        {
            var sid = new SecurityIdentifier(s);
            var b = new byte[sid.BinaryLength];
            sid.GetBinaryForm(b, 0);
            return b;
        });
        lsa.Setup(x => x.EnumerateAccountRights(It.IsAny<byte[]>())).Returns([]);
        _loginService = new AccountLoginRestrictionService(
            new GroupPolicyScriptHelper(new LogonScriptIniManager(), _log.Object, systemDir: _tempDir.Path),
            _log.Object, _accountValidation.Object);
        _lsaService = new AccountLsaRestrictionService(_log.Object, _accountValidation.Object, lsa.Object);
    }

    public void Dispose() => _tempDir.Dispose();

    // ── Read-only checks — no elevation needed ────────────────────────────────

    [Fact]
    public void IsAccountHidden_NoRegistryKey_ReturnsFalse()
    {
        var result = _loginService.IsAccountHidden("NonExistentTestUser_" + Guid.NewGuid().ToString("N"));

        Assert.False(result);
    }

    [Fact]
    public void IsLoginBlockedBySid_InvalidSid_ReturnsFalse()
    {
        var result = _loginService.IsLoginBlockedBySid("S-1-5-21-0-0-0-99999");

        Assert.False(result);
    }

    [Fact]
    public void IsLocalOnlyBySid_InvalidSid_ReturnsFalse()
    {
        // LsaEnumerateAccountRights on a non-existent SID returns no rights (or throws)
        // The service catches exceptions and returns false
        var result = _lsaService.IsLocalOnlyBySid("S-1-5-21-0-0-0-99999");

        Assert.False(result);
    }

    [Fact]
    public void IsNoBgAutostartBySid_InvalidSid_ReturnsFalse()
    {
        var result = _lsaService.IsNoBgAutostartBySid("S-1-5-21-0-0-0-99999");

        Assert.False(result);
    }

    // --- SetLoginBlockedBySid guard tests ---

    [Fact]
    public void SetLoginBlockedBySid_UnblockNonexistent_DoesNotThrow()
    {
        // Unblocking (blocked=false) skips the validation guards entirely.
        // The GP ini/wrapper files don't exist for this fake SID — should be a silent no-op.
        var ex = Record.Exception(() =>
            _loginService.SetLoginBlockedBySid("S-1-5-21-9999-9999-9999-9999", "fakeUser", blocked: false));
        Assert.Null(ex);
    }

    // ── SetNoBgAutostartBySid round-trip via mocked ILsaRightsService ─────────
    // AccountLsaRestrictionService is tested directly with an in-memory mock of ILsaRightsService
    // so no actual LSA P/Invoke (which requires elevation) is called.

    [Fact]
    public void SetNoBgAutostartBySid_ThenIsNoBgAutostartBySid_ReturnsTrue()
    {
        // Arrange
        var service = CreateServiceWithInMemoryLsa();

        // Act
        service.SetNoBgAutostartBySid("S-1-5-21-1000-2000-3000-500", blocked: true);

        // Assert
        Assert.True(service.IsNoBgAutostartBySid("S-1-5-21-1000-2000-3000-500"));
    }

    [Fact]
    public void SetNoBgAutostartBySid_UnblockAfterBlock_ReturnsFalse()
    {
        // Arrange
        var service = CreateServiceWithInMemoryLsa();

        // Act
        service.SetNoBgAutostartBySid("S-1-5-21-1000-2000-3000-500", blocked: true);
        service.SetNoBgAutostartBySid("S-1-5-21-1000-2000-3000-500", blocked: false);

        // Assert
        Assert.False(service.IsNoBgAutostartBySid("S-1-5-21-1000-2000-3000-500"));
    }

    // --- SetLocalOnlyBySid validation guard ---

    [Fact]
    public void SetLocalOnlyBySid_ValidationThrows_DoesNotAddAccountRights()
    {
        // Arrange: validation rejects the call (e.g. last-admin guard fires)
        var lsa = new Mock<ILsaRightsHelper>();
        lsa.Setup(l => l.GetSidBytes(It.IsAny<string>()))
            .Returns((string s) =>
            {
                var sid = new SecurityIdentifier(s);
                var b = new byte[sid.BinaryLength];
                sid.GetBinaryForm(b, 0);
                return b;
            });
        var validation = new Mock<IAccountValidationService>();
        validation
            .Setup(v => v.ValidateNotLastAdmin(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("Last admin — cannot restrict."));
        var service = new AccountLsaRestrictionService(_log.Object, validation.Object, lsa.Object);

        // Act & Assert: validation exception propagates to caller
        Assert.Throws<InvalidOperationException>(() =>
            service.SetLocalOnlyBySid("S-1-5-21-1000-2000-3000-500", localOnly: true));

        // AddAccountRights must never be called when validation rejects the operation
        lsa.Verify(l => l.AddAccountRights(It.IsAny<byte[]>(), It.IsAny<string[]>()), Times.Never);
    }

    // ── SetLocalOnlyBySid round-trip via mocked ILsaRightsService ────────────

    [Fact]
    public void SetLocalOnlyBySid_ThenIsLocalOnlyBySid_ReturnsTrue()
    {
        // Arrange
        var service = CreateServiceWithInMemoryLsa();

        // Act
        service.SetLocalOnlyBySid("S-1-5-21-1000-2000-3000-500", localOnly: true);

        // Assert
        Assert.True(service.IsLocalOnlyBySid("S-1-5-21-1000-2000-3000-500"));
    }

    [Fact]
    public void SetLocalOnlyBySid_UnblockAfterBlock_ReturnsFalse()
    {
        // Arrange
        var service = CreateServiceWithInMemoryLsa();

        // Act
        service.SetLocalOnlyBySid("S-1-5-21-1000-2000-3000-500", localOnly: true);
        service.SetLocalOnlyBySid("S-1-5-21-1000-2000-3000-500", localOnly: false);

        // Assert
        Assert.False(service.IsLocalOnlyBySid("S-1-5-21-1000-2000-3000-500"));
    }

    // --- GetNoLogonState / GetNoBgAutostartState partial state ---

    [Fact]
    public void GetNoBgAutostartState_InvalidSid_ReturnsFalse()
    {
        // An invalid SID has neither right — GetRightsState returns false (count == 0), not null
        var result = _lsaService.GetNoBgAutostartState("S-1-5-21-0-0-0-99999");
        Assert.False(result);
    }

    [Fact]
    public void GetLocalOnlyState_InvalidSid_ReturnsFalse()
    {
        var result = _lsaService.GetLocalOnlyState("S-1-5-21-0-0-0-99999");
        Assert.False(result);
    }

    // ── GetRightsState partial-state null return ──────────────────────────────

    [Fact]
    public void GetLocalOnlyState_OnlyOneOfTwoRequiredRightsPresent_ReturnsNull()
    {
        // Arrange: a service where only one of the two required rights for local-only is present.
        // Local-only requires both SeDenyNetworkLogonRight and SeDenyRemoteInteractiveLogonRight.
        // Seeding only the first right produces the "partial" state — neither true nor false.
        var rights = new List<string> { LsaRightsHelper.SeDenyNetworkLogonRight };
        var lsa = new Mock<ILsaRightsHelper>();
        lsa.Setup(l => l.GetSidBytes(It.IsAny<string>()))
            .Returns((string s) =>
            {
                var sid = new SecurityIdentifier(s);
                var b = new byte[sid.BinaryLength];
                sid.GetBinaryForm(b, 0);
                return b;
            });
        lsa.Setup(l => l.EnumerateAccountRights(It.IsAny<byte[]>()))
            .Returns(() => rights.ToList());
        var service = new AccountLsaRestrictionService(_log.Object, new Mock<IAccountValidationService>().Object, lsa.Object);

        // Act
        var state = service.GetLocalOnlyState("S-1-5-21-1000-2000-3000-500");

        // Assert: partial presence → null (neither fully on nor fully off)
        Assert.Null(state);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="AccountLsaRestrictionService"/> backed by an in-memory mock
    /// <see cref="ILsaRightsHelper"/> and a no-op <see cref="IAccountValidationService"/> —
    /// no LSA P/Invoke, no elevation, no system-state dependency.
    /// </summary>
    private AccountLsaRestrictionService CreateServiceWithInMemoryLsa()
    {
        var rights = new List<string>();
        var lsa = new Mock<ILsaRightsHelper>();
        lsa.Setup(l => l.GetSidBytes(It.IsAny<string>()))
            .Returns((string s) =>
            {
                var sid = new SecurityIdentifier(s);
                var b = new byte[sid.BinaryLength];
                sid.GetBinaryForm(b, 0);
                return b;
            });
        lsa.Setup(l => l.EnumerateAccountRights(It.IsAny<byte[]>()))
            .Returns(() => rights.ToList());
        lsa.Setup(l => l.AddAccountRights(It.IsAny<byte[]>(), It.IsAny<string[]>()))
            .Callback<byte[], string[]>((_, r) => rights.AddRange(r));
        lsa.Setup(l => l.RemoveAccountRights(It.IsAny<byte[]>(), It.IsAny<string[]>()))
            .Callback<byte[], string[]>((_, r) => rights.RemoveAll(r.Contains));
        // No-op validation — tests must not depend on whether "TestUser" is the last admin
        var validation = new Mock<IAccountValidationService>();
        return new AccountLsaRestrictionService(_log.Object, validation.Object, lsa.Object);
    }
}
