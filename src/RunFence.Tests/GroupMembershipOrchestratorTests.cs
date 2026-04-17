using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Groups.UI;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="GroupMembershipOrchestrator"/>.
/// </summary>
public class GroupMembershipOrchestratorTests
{
    private const string GroupSid = "S-1-5-32-1001";
    private const string MemberSid = "S-1-5-21-1000-2000-3000-1002";
    private const string MemberName = "testuser";

    private readonly Mock<ILocalGroupMembershipService> _groupMembership = new();
    private readonly Mock<IMemberPickerDialog> _memberPicker = new();
    private readonly Mock<IGroupMembershipPrompt> _prompt = new();
    private readonly Mock<ILoggingService> _log = new();

    private GroupMembershipOrchestrator CreateHandler()
        => new(_groupMembership.Object, _memberPicker.Object, _prompt.Object, _log.Object);

    // ── RemoveMember — early-exit guards ──────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RemoveMember_NullOrEmptySid_ReturnsFalseWithoutCallingService(string? memberSid)
    {
        // Arrange
        var handler = CreateHandler();

        // Act
        var result = handler.RemoveMember(GroupSid, memberSid!, MemberName, owner: null);

        // Assert: returns false without showing any confirmation dialog or calling the service
        Assert.False(result);
        _groupMembership.Verify(g => g.RemoveUserFromGroups(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never);
    }

    // ── AddMembers ────────────────────────────────────────────────────────

    [Fact]
    public void AddMembers_PickerReturnsMembers_AllSucceed_ReturnsTrue()
    {
        // Arrange
        var members = new List<LocalUserAccount> { new(MemberName, MemberSid) };
        _memberPicker.Setup(p => p.ShowPicker(It.IsAny<string>(), It.IsAny<HashSet<string>>(), null))
            .Returns(members);
        var handler = CreateHandler();

        // Act
        var result = handler.AddMembers(GroupSid, "TestGroup", new List<string>(), owner: null);

        // Assert
        Assert.True(result);
        _groupMembership.Verify(g => g.AddUserToGroups(MemberSid, MemberName,
            It.Is<List<string>>(l => l.Contains(GroupSid))), Times.Once);
        _prompt.Verify(p => p.ShowErrors(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public void AddMembers_PickerReturnsMembers_SomeFail_ShowsErrorsReturnsTrueIfAnySucceeded()
    {
        // Arrange: two members — one succeeds, one fails
        var member1 = new LocalUserAccount("user1", "S-1-5-21-1-2-3-1001");
        var member2 = new LocalUserAccount("user2", "S-1-5-21-1-2-3-1002");
        _memberPicker.Setup(p => p.ShowPicker(It.IsAny<string>(), It.IsAny<HashSet<string>>(), null))
            .Returns([member1, member2]);
        _groupMembership.Setup(g => g.AddUserToGroups(member2.Sid, member2.Username, It.IsAny<List<string>>()))
            .Throws(new Exception("Access denied"));
        var handler = CreateHandler();

        // Act
        var result = handler.AddMembers(GroupSid, "TestGroup", new List<string>(), owner: null);

        // Assert: one succeeded → returns true; errors shown
        Assert.True(result);
        _prompt.Verify(p => p.ShowErrors("Add Members", It.IsAny<IReadOnlyList<string>>()), Times.Once);
    }

    [Fact]
    public void AddMembers_PickerReturnsMembers_AllFail_ShowsErrorsReturnsFalse()
    {
        // Arrange
        var members = new List<LocalUserAccount> { new(MemberName, MemberSid) };
        _memberPicker.Setup(p => p.ShowPicker(It.IsAny<string>(), It.IsAny<HashSet<string>>(), null))
            .Returns(members);
        _groupMembership.Setup(g => g.AddUserToGroups(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()))
            .Throws(new Exception("Access denied"));
        var handler = CreateHandler();

        // Act
        var result = handler.AddMembers(GroupSid, "TestGroup", new List<string>(), owner: null);

        // Assert: all failed → returns false; errors shown
        Assert.False(result);
        _prompt.Verify(p => p.ShowErrors("Add Members", It.IsAny<IReadOnlyList<string>>()), Times.Once);
    }

    [Fact]
    public void AddMembers_PickerCancelled_ReturnsFalseNoServiceCalls()
    {
        // Arrange: picker returns null (user cancelled)
        _memberPicker.Setup(p => p.ShowPicker(It.IsAny<string>(), It.IsAny<HashSet<string>>(), null))
            .Returns((List<LocalUserAccount>?)null);
        var handler = CreateHandler();

        // Act
        var result = handler.AddMembers(GroupSid, "TestGroup", new List<string>(), owner: null);

        // Assert
        Assert.False(result);
        _groupMembership.Verify(g => g.AddUserToGroups(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never);
        _prompt.Verify(p => p.ShowErrors(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public void AddMembers_NonEmptyExistingMemberSids_PropagatesExistingMembersToPickerAsSeedSet()
    {
        // Arrange — R2_TL9: existing member SIDs must be passed to the picker so it can
        // exclude already-present members from the selectable candidates.
        var existingMemberSid = "S-1-5-21-1-2-3-9001";
        var existingMembers = new List<string> { existingMemberSid };
        HashSet<string>? capturedSeedSet = null;
        _memberPicker.Setup(p => p.ShowPicker(It.IsAny<string>(), It.IsAny<HashSet<string>>(), null))
            .Callback<string, HashSet<string>, IWin32Window?>((_, seedSet, _) => capturedSeedSet = seedSet)
            .Returns((List<LocalUserAccount>?)null);
        var handler = CreateHandler();

        // Act
        handler.AddMembers(GroupSid, "TestGroup", existingMembers, owner: null);

        // Assert: the picker received the existing member SID in its seed set
        Assert.NotNull(capturedSeedSet);
        Assert.Contains(existingMemberSid, capturedSeedSet!);
    }

    // ── RemoveMember — confirmed/declined/error ───────────────────────────

    [Fact]
    public void RemoveMember_Confirmed_ServiceSucceeds_ReturnsTrue()
    {
        // Arrange
        _prompt.Setup(p => p.ConfirmRemove(MemberName)).Returns(true);
        var handler = CreateHandler();

        // Act
        var result = handler.RemoveMember(GroupSid, MemberSid, MemberName, owner: null);

        // Assert
        Assert.True(result);
        _groupMembership.Verify(g => g.RemoveUserFromGroups(MemberSid, MemberName,
            It.Is<List<string>>(l => l.Contains(GroupSid))), Times.Once);
        _prompt.Verify(p => p.ShowErrors(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public void RemoveMember_Confirmed_ServiceThrows_ShowsErrorReturnsFalse()
    {
        // Arrange
        _prompt.Setup(p => p.ConfirmRemove(MemberName)).Returns(true);
        _groupMembership.Setup(g => g.RemoveUserFromGroups(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()))
            .Throws(new Exception("Not authorized"));
        var handler = CreateHandler();

        // Act
        var result = handler.RemoveMember(GroupSid, MemberSid, MemberName, owner: null);

        // Assert
        Assert.False(result);
        _prompt.Verify(p => p.ShowErrors("Error", It.IsAny<IReadOnlyList<string>>()), Times.Once);
    }

    [Fact]
    public void RemoveMember_Declined_ReturnsFalseNoServiceCall()
    {
        // Arrange
        _prompt.Setup(p => p.ConfirmRemove(MemberName)).Returns(false);
        var handler = CreateHandler();

        // Act
        var result = handler.RemoveMember(GroupSid, MemberSid, MemberName, owner: null);

        // Assert
        Assert.False(result);
        _groupMembership.Verify(g => g.RemoveUserFromGroups(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never);
    }
}