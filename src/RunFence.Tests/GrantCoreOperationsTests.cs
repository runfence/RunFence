using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="GrantCoreOperations"/> covering opposite-mode conflict detection
/// and SavedRights update on same-mode duplicate grants.
/// ACE and owner calls are mocked through focused NTFS services.
/// DB access is performed inline on the calling thread via a synchronous invoker.
/// </summary>
public class GrantCoreOperationsTests
{
    private const string UserSid = "S-1-5-21-1000-1000-1000-1001";
    private const string TestPath = @"C:\TestFolder\SubDir";

    private readonly Mock<IGrantAceService> _grantAceService = new();
    private readonly Mock<IFileOwnerService> _fileOwnerService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly TestFileSystemPathInfo _pathInfo = new();
    private readonly AppDatabase _database = new();
    private readonly GrantCoreOperations _ops;

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a());

    public GrantCoreOperationsTests()
    {
        var dbProvider = new LambdaDatabaseProvider(() => _database);
        var dbAccessor = new UiThreadDatabaseAccessor(dbProvider, SyncInvoker);
        _ops = new GrantCoreOperations(_grantAceService.Object, _fileOwnerService.Object,
            dbAccessor, _log.Object, _pathInfo);
    }

    // ── TC-13: opposite-mode conflict ────────────────────────────────────────

    [Fact]
    public void AddGrant_OppositeModeAlreadyExists_ThrowsInvalidOperationException()
    {
        // Arrange — Allow grant already exists for the normalized path
        var normalized = Path.GetFullPath(TestPath);
        _database.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = normalized,
            IsDeny = false // existing: Allow
        });

        // Act & Assert — attempt to add Deny: opposite mode → InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(
            () => _ops.AddGrant(UserSid, TestPath, isDeny: true));

        Assert.Contains("opposite-mode", ex.Message, StringComparison.OrdinalIgnoreCase);
        _grantAceService.Verify(n => n.ApplyAce(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<SavedRightsState>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void AddGrant_OppositeModeDenyExists_ThrowsWhenAddingAllow()
    {
        // Arrange — Deny grant exists; attempt to add Allow
        var normalized = Path.GetFullPath(TestPath);
        _database.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = normalized,
            IsDeny = true // existing: Deny
        });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => _ops.AddGrant(UserSid, TestPath, isDeny: false));

        Assert.Contains("opposite-mode", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddGrant_OppositeMode_TraverseOnlyEntryIsIgnored()
    {
        // Arrange — traverse-only entry should not count as an opposite-mode conflict
        var normalized = Path.GetFullPath(TestPath);
        _database.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = normalized,
            IsDeny = false,
            IsTraverseOnly = true // traverse-only: should NOT block adding Deny
        });

        // Act — adding Deny; traverse-only Allow should not conflict
        var result = _ops.AddGrant(UserSid, TestPath, isDeny: true);

        // Assert — grant added successfully
        Assert.False(result.AlreadyExisted);
        Assert.True(result.DatabaseModified);
        _grantAceService.Verify(n => n.ApplyAce(It.IsAny<string>(), It.IsAny<string>(),
            It.Is<bool>(d => d), It.IsAny<SavedRightsState>(), It.IsAny<bool>()), Times.Once);
    }

    // ── TC-13: SavedRights update on same-mode duplicate ─────────────────────

    [Fact]
    public void AddGrant_SameModeAlreadyExists_UpdatesSavedRightsAndReturnsAlreadyExisted()
    {
        // Arrange — Allow grant already exists
        var normalized = Path.GetFullPath(TestPath);
        var existingEntry = new GrantedPathEntry
        {
            Path = normalized,
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        };
        _database.GetOrCreateAccount(UserSid).Grants.Add(existingEntry);

        // New rights with Write=true (different from default)
        var newRights = new SavedRightsState(Execute: true, Write: true, Read: true, Special: false, Own: false);

        // Act
        var result = _ops.AddGrant(UserSid, TestPath, isDeny: false, savedRights: newRights);

        // Assert — returns AlreadyExisted=true and updates SavedRights in the DB entry
        Assert.True(result.AlreadyExisted);
        Assert.True(result.DatabaseModified);
        Assert.Equal(newRights, existingEntry.SavedRights);

        // NTFS ACE re-applied with the new rights
        _grantAceService.Verify(n => n.ApplyAce(normalized, UserSid, false, newRights, It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void AddGrant_SameModeDenyAlreadyExists_UpdatesSavedRightsForDeny()
    {
        // Arrange — Deny grant already exists
        var normalized = Path.GetFullPath(TestPath);
        var existingEntry = new GrantedPathEntry
        {
            Path = normalized,
            IsDeny = true,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: true)
        };
        _database.GetOrCreateAccount(UserSid).Grants.Add(existingEntry);

        var newRights = new SavedRightsState(Execute: true, Write: true, Read: true, Special: true, Own: false);

        // Act
        var result = _ops.AddGrant(UserSid, TestPath, isDeny: true, savedRights: newRights);

        // Assert
        Assert.True(result.AlreadyExisted);
        Assert.Equal(newRights, existingEntry.SavedRights);
        _grantAceService.Verify(n => n.ApplyAce(normalized, UserSid, true, newRights, It.IsAny<bool>()), Times.Once);
    }

    // ── ValidateGrant — same rule but read-only ───────────────────────────────

    [Fact]
    public void ValidateGrant_OppositeModeExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var normalized = Path.GetFullPath(TestPath);
        _database.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = normalized,
            IsDeny = false
        });

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => _ops.ValidateGrant(UserSid, TestPath, isDeny: true));
    }

    [Fact]
    public void ValidateGrant_SameModeExists_ThrowsInvalidOperationException()
    {
        // Same-mode duplicate is also invalid for validate
        var normalized = Path.GetFullPath(TestPath);
        _database.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = normalized,
            IsDeny = true
        });

        Assert.Throws<InvalidOperationException>(
            () => _ops.ValidateGrant(UserSid, TestPath, isDeny: true));
    }

    [Fact]
    public void ValidateGrant_NoConflict_DoesNotThrow()
    {
        // Act & Assert — no existing entry, no exception
        _ops.ValidateGrant(UserSid, TestPath, isDeny: false);
    }

    // ── UpdateGrant — SavedRights update ─────────────────────────────────────

    [Fact]
    public void UpdateGrant_ExistingEntry_UpdatesSavedRightsInDb()
    {
        // Arrange
        var normalized = Path.GetFullPath(TestPath);
        var existingEntry = new GrantedPathEntry
        {
            Path = normalized,
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        };
        _database.GetOrCreateAccount(UserSid).Grants.Add(existingEntry);

        var newRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: true);

        // Act
        _ops.UpdateGrant(UserSid, TestPath, isDeny: false, savedRights: newRights);

        // Assert — DB entry updated, NTFS ACE re-applied
        Assert.Equal(newRights, existingEntry.SavedRights);
        _grantAceService.Verify(n => n.ApplyAce(normalized, UserSid, false, newRights, It.IsAny<bool>()), Times.Once);
    }

    // ── AddGrant — new entry created correctly ────────────────────────────────

    [Fact]
    public void AddGrant_NewEntry_AddsToDbWithSavedRights()
    {
        // Arrange
        var normalized = Path.GetFullPath(TestPath);
        var rights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false);

        // Act
        var result = _ops.AddGrant(UserSid, TestPath, isDeny: false, savedRights: rights);

        // Assert
        Assert.False(result.AlreadyExisted);
        Assert.True(result.DatabaseModified);

        var account = _database.GetAccount(UserSid)!;
        var entry = account.Grants.FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.NotNull(entry);
        Assert.Equal(normalized, entry.Path);
        Assert.Equal(rights, entry.SavedRights);
    }
}
