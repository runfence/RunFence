using System.Security.Principal;
using Moq;
using RunFence.Account;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class AccountValidationServiceTests
{
    private readonly Mock<ILoggingService> _log = new();
    private readonly AccountValidationService _service;

    public AccountValidationServiceTests()
    {
        _service = new AccountValidationService(_log.Object);
    }

    [Fact]
    public void ValidateNotCurrentAccount_CurrentSid_Throws()
    {
        var currentSid = WindowsIdentity.GetCurrent().User!.Value;

        var ex = Assert.Throws<InvalidOperationException>(() => _service.ValidateNotCurrentAccount(currentSid, "test"));

        Assert.Contains("current account", ex.Message);
    }

    [Fact]
    public void ValidateNotCurrentAccount_DifferentSid_DoesNotThrow()
    {
        var fakeSid = "S-1-5-21-0-0-0-99999";

        _service.ValidateNotCurrentAccount(fakeSid, "test");
        // No exception means success
    }

    // --- ValidateNotLastAdmin ---

    [Fact]
    public void ValidateNotLastAdmin_NonExistentSid_DoesNotThrow()
    {
        // A SID that definitely is not in the local Administrators group.
        // IsLastAdminAccount returns false, so no exception is thrown.
        _service.ValidateNotLastAdmin("S-1-5-21-0-0-0-99999", "test");
    }

    // --- ValidateNotInteractiveUser ---

    [Fact]
    public void ValidateNotInteractiveUser_UnrelatedSid_DoesNotThrow()
    {
        // A SID that will never match the interactive user SID.
        var fakeSid = "S-1-5-21-0-0-0-99998";

        _service.ValidateNotInteractiveUser(fakeSid, "test");
        // No exception: the SID does not match the interactive user
    }

    // --- ValidateNoRunningProcesses ---

    [Fact]
    public void ValidateNoRunningProcesses_UnknownSid_DoesNotThrow()
    {
        // No running process is owned by this synthetic SID.
        var fakeSid = "S-1-5-21-0-0-0-99997";

        _service.ValidateNoRunningProcesses(fakeSid, "test");
        // No exception: no processes found for this SID
    }

    // --- GetProcessesRunningAsSid ---

    [Fact]
    public void GetProcessesRunningAsSid_UnknownSid_ReturnsEmpty()
    {
        var fakeSid = "S-1-5-21-0-0-0-99996";

        var result = _service.GetProcessesRunningAsSid(fakeSid);

        Assert.Empty(result);
    }

    [Fact]
    public void GetProcessesRunningAsSid_CurrentUserSid_ReturnsAtLeastOneProcess()
    {
        // The current user must own at least one process (this test process itself).
        var currentSid = WindowsIdentity.GetCurrent().User!.Value;

        var result = _service.GetProcessesRunningAsSid(currentSid);

        Assert.NotEmpty(result);
    }
}