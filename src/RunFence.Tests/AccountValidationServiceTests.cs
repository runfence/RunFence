using System.Security.Principal;
using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AccountValidationServiceTests
{
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ILocalGroupMembershipService> _groupMembership = new();
    private readonly Mock<IProcessListService> _processListService = new();
    private readonly AccountValidationService _service;

    public AccountValidationServiceTests()
    {
        _groupMembership.Setup(s => s.GetMembersOfGroup(It.IsAny<string>())).Returns(new List<LocalUserAccount>());
        _groupMembership.Setup(s => s.IsLocalGroup(It.IsAny<string>())).Returns(false);
        _groupMembership.Setup(s => s.IsUserAccountEnabled(It.IsAny<string>())).Returns(true);
        _processListService.Setup(s => s.GetProcessesForSid(It.IsAny<string>())).Returns([]);
        _service = new AccountValidationService(_log.Object, _groupMembership.Object, _processListService.Object);
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

    [Fact]
    public void ValidateNotLastAdmin_IsLastAdmin_Throws()
    {
        // Arrange: target is the only enabled admin account
        var targetSid = "S-1-5-21-0-0-0-99994";
        _groupMembership.Setup(s => s.GetMembersOfGroup("S-1-5-32-544"))
            .Returns([new LocalUserAccount("admin_user", targetSid)]);
        _groupMembership.Setup(s => s.IsLocalGroup(targetSid)).Returns(false);
        _groupMembership.Setup(s => s.IsUserAccountEnabled("admin_user")).Returns(true);

        var ex = Assert.Throws<InvalidOperationException>(() => _service.ValidateNotLastAdmin(targetSid, "delete"));

        Assert.Contains("last administrator", ex.Message);
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

    [Fact]
    public void ValidateNoRunningProcesses_HasRunningProcesses_Throws()
    {
        // Arrange: process list service reports a running process for the target SID
        var targetSid = "S-1-5-21-0-0-0-99993";
        _processListService.Setup(s => s.GetProcessesForSid(targetSid))
            .Returns([new ProcessInfo(1234, @"C:\Windows\System32\notepad.exe", null)]);

        var ex = Assert.Throws<InvalidOperationException>(() => _service.ValidateNoRunningProcesses(targetSid, "delete"));

        Assert.Contains("running processes", ex.Message);
        Assert.Contains("notepad", ex.Message);
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
    public void GetProcessesRunningAsSid_WhenProcessListServiceReturnsProcesses_ReturnsNames()
    {
        // Verify that GetProcessesRunningAsSid returns names from IProcessListService results.
        var sid = "S-1-5-21-0-0-0-99995";
        _processListService.Setup(s => s.GetProcessesForSid(sid))
            .Returns([new ProcessInfo(1234, @"C:\Program Files\test.exe", null)]);

        var result = _service.GetProcessesRunningAsSid(sid);

        Assert.Single(result);
        Assert.Equal("test", result[0]);
    }
}