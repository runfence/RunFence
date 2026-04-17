using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class SavedRightsComparerTests
{
    private readonly SavedRightsComparer _comparer = SavedRightsComparer.Instance;

    // --- Helper factories ---

    private static GrantRightsState AllowState(
        RightCheckState execute = RightCheckState.Unchecked,
        RightCheckState write = RightCheckState.Unchecked,
        RightCheckState special = RightCheckState.Unchecked,
        RightCheckState isAccountOwner = RightCheckState.Unchecked,
        int directAllowAceCount = 1) =>
        new(AllowExecute: execute, AllowWrite: write, AllowSpecial: special,
            DenyRead: RightCheckState.Unchecked, DenyExecute: RightCheckState.Unchecked,
            DenyWrite: RightCheckState.Unchecked, DenySpecial: RightCheckState.Unchecked,
            IsAccountOwner: isAccountOwner, IsAdminOwner: false,
            DirectAllowAceCount: directAllowAceCount, DirectDenyAceCount: 0);

    private static GrantRightsState DenyState(
        RightCheckState denyRead = RightCheckState.Unchecked,
        RightCheckState denyExecute = RightCheckState.Unchecked,
        RightCheckState isAccountOwner = RightCheckState.Unchecked,
        bool isAdminOwner = false,
        int directDenyAceCount = 1) =>
        new(AllowExecute: RightCheckState.Unchecked, AllowWrite: RightCheckState.Unchecked,
            AllowSpecial: RightCheckState.Unchecked,
            DenyRead: denyRead, DenyExecute: denyExecute,
            DenyWrite: RightCheckState.Checked, DenySpecial: RightCheckState.Checked,
            IsAccountOwner: isAccountOwner, IsAdminOwner: isAdminOwner,
            DirectAllowAceCount: 0, DirectDenyAceCount: directDenyAceCount);

    private static GrantedPathEntry AllowEntry(bool execute = false, bool write = false, bool read = true, bool special = false, bool own = false) =>
        new() { IsDeny = false, SavedRights = new SavedRightsState(execute, write, read, special, own) };

    private static GrantedPathEntry DenyEntry(bool execute = false, bool write = true, bool read = false, bool special = true, bool own = false) =>
        new() { IsDeny = true, SavedRights = new SavedRightsState(execute, write, read, special, own) };

    // --- MatchesSavedRights: null SavedRights ---

    [Fact]
    public void MatchesSavedRights_NullSavedRights_ReturnsFalse()
    {
        var entry = new GrantedPathEntry { IsDeny = false, SavedRights = null };
        var state = AllowState();

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    // --- MatchesSavedRights: Allow mode ---

    [Fact]
    public void MatchesSavedRights_AllowMode_AllRightsMatch_ReturnsTrue()
    {
        var entry = AllowEntry(execute: true, write: false, read: true, special: false, own: false);
        var state = AllowState(execute: RightCheckState.Checked, write: RightCheckState.Unchecked, special: RightCheckState.Unchecked);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Theory]
    [InlineData(true, RightCheckState.Unchecked)] // saved=true, actual=false → drift
    [InlineData(false, RightCheckState.Checked)] // saved=false, actual=true → drift
    public void MatchesSavedRights_AllowMode_ExecuteDrift_ReturnsFalse(bool savedExecute, RightCheckState actualExecute)
    {
        var entry = AllowEntry(execute: savedExecute);
        var state = AllowState(execute: actualExecute);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_AllowMode_WriteDrift_ReturnsFalse()
    {
        var entry = AllowEntry(write: true);
        var state = AllowState(write: RightCheckState.Unchecked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_AllowMode_SpecialDrift_ReturnsFalse()
    {
        var entry = AllowEntry(special: true);
        var state = AllowState(special: RightCheckState.Unchecked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_AllowMode_NoAce_ReturnsFalse()
    {
        var entry = AllowEntry();
        var state = AllowState(directAllowAceCount: 0);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_AllowMode_DuplicateAces_ReturnsFalse()
    {
        var entry = AllowEntry();
        var state = AllowState(directAllowAceCount: 2);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    // --- MatchesSavedRights: Deny mode ---

    [Fact]
    public void MatchesSavedRights_DenyMode_AllRightsMatch_ReturnsTrue()
    {
        // Write+Special are always-on in deny mode → not compared. Only Execute and Read.
        var entry = DenyEntry(execute: false, write: true, read: true, special: true);
        var state = DenyState(denyRead: RightCheckState.Checked, denyExecute: RightCheckState.Unchecked);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_DenyMode_WriteAndSpecialAlwaysOn_NotCompared()
    {
        // Even if the saved Write=false and Special=false (which shouldn't happen in practice),
        // the deny-mode comparer only compares Execute and Read, not Write/Special.
        var entry = new GrantedPathEntry
        {
            IsDeny = true,
            SavedRights = new SavedRightsState(Execute: false, Write: false, Read: false, Special: false, Own: false)
        };
        var state = DenyState(denyRead: RightCheckState.Unchecked, denyExecute: RightCheckState.Unchecked);

        // Write/Special mismatch is NOT detected → should return true (only Execute+Read compared)
        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_DenyMode_ExecuteDrift_ReturnsFalse()
    {
        var entry = DenyEntry(execute: true);
        var state = DenyState(denyExecute: RightCheckState.Unchecked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_DenyMode_ReadDrift_ReturnsFalse()
    {
        var entry = DenyEntry(read: true);
        var state = DenyState(denyRead: RightCheckState.Unchecked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_DenyMode_NoAce_ReturnsFalse()
    {
        var entry = DenyEntry();
        var state = DenyState(directDenyAceCount: 0);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_DenyMode_DuplicateAces_ReturnsFalse()
    {
        var entry = DenyEntry();
        var state = DenyState(directDenyAceCount: 2);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    // --- Own comparison: Allow mode ---

    [Fact]
    public void MatchesSavedRights_AllowUnchecked_ButActualOwner_ReturnsFalse()
    {
        // Allow+unchecked (saved.Own=false) but ACL owner IS this SID → mismatch
        var entry = AllowEntry(own: false);
        var state = AllowState(isAccountOwner: RightCheckState.Checked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_AllowChecked_ButNotActualOwner_ReturnsFalse()
    {
        // Allow+checked (saved.Own=true) but ACL owner is NOT this SID → mismatch
        var entry = AllowEntry(own: true);
        var state = AllowState(isAccountOwner: RightCheckState.Unchecked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_AllowChecked_AndActualOwner_ReturnsTrue()
    {
        var entry = AllowEntry(own: true);
        var state = AllowState(isAccountOwner: RightCheckState.Checked);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    // --- Own comparison: Deny mode ---

    [Fact]
    public void MatchesSavedRights_DenyUnchecked_NeverOwnerMismatch_ReturnsTrue()
    {
        // Deny+unchecked → never a mismatch regardless of actual owner
        var entry = DenyEntry(own: false);
        var state = DenyState(isAccountOwner: RightCheckState.Checked); // account owns — but we don't care

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_DenyUnchecked_AdminIsOwner_ReturnsTrue()
    {
        // Deny+unchecked → never a mismatch regardless of actual owner (even admin)
        var entry = DenyEntry(own: false);
        var state = DenyState(isAdminOwner: true);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_DenyChecked_AccountOwner_ReturnsFalse()
    {
        // Deny+checked (wants admin owner) but this SID is the owner → mismatch
        var entry = DenyEntry(own: true);
        var state = DenyState(isAccountOwner: RightCheckState.Checked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_DenyChecked_AdminIsOwner_ReturnsTrue()
    {
        // Deny+checked wants admin owner. Admin owns. This SID does not own → not a mismatch.
        var entry = DenyEntry(own: true);
        var state = DenyState(isAccountOwner: RightCheckState.Unchecked, isAdminOwner: true);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_DenyChecked_ThirdPartyOwner_NotMismatch()
    {
        // Deny+checked but owner is someone else (not this SID, not admin) → NOT a mismatch
        var entry = DenyEntry(own: true);
        var state = DenyState(isAccountOwner: RightCheckState.Unchecked, isAdminOwner: false);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false, isFolder: true));
    }

    // --- Own comparison: Container entries skip Own ---

    [Fact]
    public void MatchesSavedRights_Container_AllowMode_SkipsOwnComparison()
    {
        // Own mismatch would normally trigger false, but container entries skip Own
        var entry = AllowEntry(own: false);
        var state = AllowState(isAccountOwner: RightCheckState.Checked); // account owns (would be mismatch for non-container)

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: true, isFolder: true));
    }

    [Fact]
    public void MatchesSavedRights_Container_DenyMode_SkipsOwnComparison()
    {
        var entry = DenyEntry(own: true);
        var state = DenyState(isAccountOwner: RightCheckState.Checked); // would be mismatch for non-container

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: true, isFolder: true));
    }

    // --- AutoPopulateMissingSavedRights ---

    [Fact]
    public void AutoPopulate_AllowMode_NullSavedRights_PopulatesFromNtfsState()
    {
        var entry = new GrantedPathEntry { IsDeny = false, SavedRights = null };
        var state = AllowState(
            execute: RightCheckState.Checked,
            write: RightCheckState.Checked,
            special: RightCheckState.Unchecked,
            isAccountOwner: RightCheckState.Checked);

        var populated = _comparer.AutoPopulateMissingSavedRights([entry], _ => state, isContainer: false);

        Assert.Single(populated);
        Assert.Same(entry, populated[0]);
        Assert.NotNull(entry.SavedRights);
        Assert.True(entry.SavedRights!.Execute);
        Assert.True(entry.SavedRights.Write);
        Assert.True(entry.SavedRights.Read); // always-on in allow mode
        Assert.False(entry.SavedRights.Special);
        Assert.True(entry.SavedRights.Own);
    }

    [Fact]
    public void AutoPopulate_DenyMode_NullSavedRights_PopulatesFromNtfsState()
    {
        var entry = new GrantedPathEntry { IsDeny = true, SavedRights = null };
        var state = DenyState(
            denyRead: RightCheckState.Checked,
            denyExecute: RightCheckState.Checked,
            isAdminOwner: true);

        var populated = _comparer.AutoPopulateMissingSavedRights([entry], _ => state, isContainer: false);

        Assert.Single(populated);
        Assert.NotNull(entry.SavedRights);
        Assert.True(entry.SavedRights!.Execute);
        Assert.True(entry.SavedRights.Write); // always-on in deny mode
        Assert.True(entry.SavedRights.Read);
        Assert.True(entry.SavedRights.Special); // always-on in deny mode
        Assert.True(entry.SavedRights.Own); // IsAdminOwner = true
    }

    [Fact]
    public void AutoPopulate_NonNullSavedRights_Skipped()
    {
        var existing = new SavedRightsState(true, false, true, false, false);
        var entry = new GrantedPathEntry { IsDeny = false, SavedRights = existing };
        var state = AllowState(execute: RightCheckState.Unchecked);

        var populated = _comparer.AutoPopulateMissingSavedRights([entry], _ => state, isContainer: false);

        Assert.Empty(populated);
        Assert.Same(existing, entry.SavedRights); // unchanged
    }

    [Fact]
    public void AutoPopulate_ReadRightsReturnsNull_EntrySkipped()
    {
        var entry = new GrantedPathEntry { IsDeny = false, SavedRights = null };

        var populated = _comparer.AutoPopulateMissingSavedRights([entry], _ => null, isContainer: false);

        Assert.Empty(populated);
        Assert.Null(entry.SavedRights);
    }

    [Fact]
    public void AutoPopulate_Container_OwnAlwaysFalse()
    {
        var entry = new GrantedPathEntry { IsDeny = false, SavedRights = null };
        var state = AllowState(isAccountOwner: RightCheckState.Checked); // account is owner

        _comparer.AutoPopulateMissingSavedRights([entry], _ => state, isContainer: true);

        Assert.False(entry.SavedRights!.Own); // containers never have Own
    }

    [Fact]
    public void AutoPopulate_Container_DenyMode_OwnAlwaysFalse()
    {
        var entry = new GrantedPathEntry { IsDeny = true, SavedRights = null };
        var state = DenyState(isAdminOwner: true);

        _comparer.AutoPopulateMissingSavedRights([entry], _ => state, isContainer: true);

        Assert.False(entry.SavedRights!.Own);
    }

    [Fact]
    public void AutoPopulate_MixedEntries_OnlyNullSavedRightsPopulated()
    {
        var entryWithRights = new GrantedPathEntry
        {
            IsDeny = false,
            SavedRights = new SavedRightsState(false, false, true, false, false)
        };
        var entryWithoutRights = new GrantedPathEntry { IsDeny = false, SavedRights = null };

        var state = AllowState();
        var populated = _comparer.AutoPopulateMissingSavedRights(
            [entryWithRights, entryWithoutRights], _ => state, isContainer: false);

        Assert.Single(populated);
        Assert.Same(entryWithoutRights, populated[0]);
    }

    // --- FromNtfsState: isFolder=false ---

    [Fact]
    public void FromNtfsState_AllowMode_FileNotFolder_UsesFileWriteMask()
    {
        // isFolder=false: Write uses WriteFileMask (includes Delete, not DeleteSubdirectoriesAndFiles)
        // and Special uses SpecialFileMask (no Delete — Delete is in WriteFileMask for files).
        // With Write+Special checked, both masks are different from folder equivalents.
        var state = AllowState(
            write: RightCheckState.Checked,
            special: RightCheckState.Checked);

        var result = SavedRightsComparer.FromNtfsState(state, isDeny: false, isContainer: false, isFolder: false);

        Assert.True(result.Read);
        Assert.True(result.Write);
        Assert.True(result.Special);
        Assert.False(result.Execute);
        Assert.False(result.Own);
    }

    [Fact]
    public void FromNtfsState_AllowMode_FolderAndFile_SameForReadExecuteOwn()
    {
        // Read is always-on; Execute and Own are not path-type-specific.
        var state = AllowState(
            execute: RightCheckState.Checked,
            isAccountOwner: RightCheckState.Checked);

        var resultFolder = SavedRightsComparer.FromNtfsState(state, isDeny: false, isContainer: false, isFolder: true);
        var resultFile = SavedRightsComparer.FromNtfsState(state, isDeny: false, isContainer: false, isFolder: false);

        Assert.True(resultFolder.Execute);
        Assert.True(resultFile.Execute);
        Assert.True(resultFolder.Read);
        Assert.True(resultFile.Read);
        Assert.True(resultFolder.Own);
        Assert.True(resultFile.Own);
    }

    [Fact]
    public void FromNtfsState_DenyMode_FileNotFolder_UsesFileWriteAndSpecialMasks()
    {
        // isFolder=false: Write+Special are always-on in deny mode, but the underlying masks differ.
        // From the result perspective, Write=true and Special=true regardless (they are always-on).
        // Execute and Read are configurable — verify they're preserved correctly for files too.
        var state = DenyState(
            denyRead: RightCheckState.Checked,
            denyExecute: RightCheckState.Checked);

        var result = SavedRightsComparer.FromNtfsState(state, isDeny: true, isContainer: false, isFolder: false);

        Assert.True(result.Read);
        Assert.True(result.Execute);
        Assert.True(result.Write);   // always-on in deny mode
        Assert.True(result.Special); // always-on in deny mode
        Assert.False(result.Own);    // IsAdminOwner=false in DenyState default
    }

    [Fact]
    public void FromNtfsState_DenyMode_File_OwnFromAdminOwner()
    {
        // For deny mode on a file, Own comes from IsAdminOwner, same as folders.
        var state = DenyState(isAdminOwner: true);

        var resultFile = SavedRightsComparer.FromNtfsState(state, isDeny: true, isContainer: false, isFolder: false);
        var resultFolder = SavedRightsComparer.FromNtfsState(state, isDeny: true, isContainer: false, isFolder: true);

        Assert.True(resultFile.Own);
        Assert.True(resultFolder.Own);
    }

    [Fact]
    public void FromNtfsState_AllowMode_Container_OwnAlwaysFalse_BothPathTypes()
    {
        // Containers never have Own=true regardless of IsAccountOwner and isFolder value.
        var state = AllowState(isAccountOwner: RightCheckState.Checked);

        var resultFile = SavedRightsComparer.FromNtfsState(state, isDeny: false, isContainer: true, isFolder: false);
        var resultFolder = SavedRightsComparer.FromNtfsState(state, isDeny: false, isContainer: true, isFolder: true);

        Assert.False(resultFile.Own);
        Assert.False(resultFolder.Own);
    }
}
