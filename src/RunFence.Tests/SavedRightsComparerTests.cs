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
        CheckState execute = CheckState.Unchecked,
        CheckState write = CheckState.Unchecked,
        CheckState special = CheckState.Unchecked,
        CheckState isAccountOwner = CheckState.Unchecked,
        int directAllowAceCount = 1) =>
        new(AllowExecute: execute, AllowWrite: write, AllowSpecial: special,
            DenyRead: CheckState.Unchecked, DenyExecute: CheckState.Unchecked,
            DenyWrite: CheckState.Unchecked, DenySpecial: CheckState.Unchecked,
            IsAccountOwner: isAccountOwner, IsAdminOwner: false,
            DirectAllowAceCount: directAllowAceCount, DirectDenyAceCount: 0);

    private static GrantRightsState DenyState(
        CheckState denyRead = CheckState.Unchecked,
        CheckState denyExecute = CheckState.Unchecked,
        CheckState isAccountOwner = CheckState.Unchecked,
        bool isAdminOwner = false,
        int directDenyAceCount = 1) =>
        new(AllowExecute: CheckState.Unchecked, AllowWrite: CheckState.Unchecked,
            AllowSpecial: CheckState.Unchecked,
            DenyRead: denyRead, DenyExecute: denyExecute,
            DenyWrite: CheckState.Checked, DenySpecial: CheckState.Checked,
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

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    // --- MatchesSavedRights: Allow mode ---

    [Fact]
    public void MatchesSavedRights_AllowMode_AllRightsMatch_ReturnsTrue()
    {
        var entry = AllowEntry(execute: true, write: false, read: true, special: false, own: false);
        var state = AllowState(execute: CheckState.Checked, write: CheckState.Unchecked, special: CheckState.Unchecked);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Theory]
    [InlineData(true, CheckState.Unchecked)] // saved=true, actual=false → drift
    [InlineData(false, CheckState.Checked)] // saved=false, actual=true → drift
    public void MatchesSavedRights_AllowMode_ExecuteDrift_ReturnsFalse(bool savedExecute, CheckState actualExecute)
    {
        var entry = AllowEntry(execute: savedExecute);
        var state = AllowState(execute: actualExecute);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_AllowMode_WriteDrift_ReturnsFalse()
    {
        var entry = AllowEntry(write: true);
        var state = AllowState(write: CheckState.Unchecked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_AllowMode_SpecialDrift_ReturnsFalse()
    {
        var entry = AllowEntry(special: true);
        var state = AllowState(special: CheckState.Unchecked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_AllowMode_NoAce_ReturnsFalse()
    {
        var entry = AllowEntry();
        var state = AllowState(directAllowAceCount: 0);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_AllowMode_DuplicateAces_ReturnsFalse()
    {
        var entry = AllowEntry();
        var state = AllowState(directAllowAceCount: 2);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    // --- MatchesSavedRights: Deny mode ---

    [Fact]
    public void MatchesSavedRights_DenyMode_AllRightsMatch_ReturnsTrue()
    {
        // Write+Special are always-on in deny mode → not compared. Only Execute and Read.
        var entry = DenyEntry(execute: false, write: true, read: true, special: true);
        var state = DenyState(denyRead: CheckState.Checked, denyExecute: CheckState.Unchecked);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false));
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
        var state = DenyState(denyRead: CheckState.Unchecked, denyExecute: CheckState.Unchecked);

        // Write/Special mismatch is NOT detected → should return true (only Execute+Read compared)
        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_DenyMode_ExecuteDrift_ReturnsFalse()
    {
        var entry = DenyEntry(execute: true);
        var state = DenyState(denyExecute: CheckState.Unchecked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_DenyMode_ReadDrift_ReturnsFalse()
    {
        var entry = DenyEntry(read: true);
        var state = DenyState(denyRead: CheckState.Unchecked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_DenyMode_NoAce_ReturnsFalse()
    {
        var entry = DenyEntry();
        var state = DenyState(directDenyAceCount: 0);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_DenyMode_DuplicateAces_ReturnsFalse()
    {
        var entry = DenyEntry();
        var state = DenyState(directDenyAceCount: 2);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    // --- Own comparison: Allow mode ---

    [Fact]
    public void MatchesSavedRights_AllowUnchecked_ButActualOwner_ReturnsFalse()
    {
        // Allow+unchecked (saved.Own=false) but ACL owner IS this SID → mismatch
        var entry = AllowEntry(own: false);
        var state = AllowState(isAccountOwner: CheckState.Checked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_AllowChecked_ButNotActualOwner_ReturnsFalse()
    {
        // Allow+checked (saved.Own=true) but ACL owner is NOT this SID → mismatch
        var entry = AllowEntry(own: true);
        var state = AllowState(isAccountOwner: CheckState.Unchecked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_AllowChecked_AndActualOwner_ReturnsTrue()
    {
        var entry = AllowEntry(own: true);
        var state = AllowState(isAccountOwner: CheckState.Checked);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    // --- Own comparison: Deny mode ---

    [Fact]
    public void MatchesSavedRights_DenyUnchecked_NeverOwnerMismatch_ReturnsTrue()
    {
        // Deny+unchecked → never a mismatch regardless of actual owner
        var entry = DenyEntry(own: false);
        var state = DenyState(isAccountOwner: CheckState.Checked); // account owns — but we don't care

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_DenyUnchecked_AdminIsOwner_ReturnsTrue()
    {
        // Deny+unchecked → never a mismatch regardless of actual owner (even admin)
        var entry = DenyEntry(own: false);
        var state = DenyState(isAdminOwner: true);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_DenyChecked_AccountOwner_ReturnsFalse()
    {
        // Deny+checked (wants admin owner) but this SID is the owner → mismatch
        var entry = DenyEntry(own: true);
        var state = DenyState(isAccountOwner: CheckState.Checked);

        Assert.False(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_DenyChecked_AdminIsOwner_ReturnsTrue()
    {
        // Deny+checked wants admin owner. Admin owns. This SID does not own → not a mismatch.
        var entry = DenyEntry(own: true);
        var state = DenyState(isAccountOwner: CheckState.Unchecked, isAdminOwner: true);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    [Fact]
    public void MatchesSavedRights_DenyChecked_ThirdPartyOwner_NotMismatch()
    {
        // Deny+checked but owner is someone else (not this SID, not admin) → NOT a mismatch
        var entry = DenyEntry(own: true);
        var state = DenyState(isAccountOwner: CheckState.Unchecked, isAdminOwner: false);

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: false));
    }

    // --- Own comparison: Container entries skip Own ---

    [Fact]
    public void MatchesSavedRights_Container_AllowMode_SkipsOwnComparison()
    {
        // Own mismatch would normally trigger false, but container entries skip Own
        var entry = AllowEntry(own: false);
        var state = AllowState(isAccountOwner: CheckState.Checked); // account owns (would be mismatch for non-container)

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: true));
    }

    [Fact]
    public void MatchesSavedRights_Container_DenyMode_SkipsOwnComparison()
    {
        var entry = DenyEntry(own: true);
        var state = DenyState(isAccountOwner: CheckState.Checked); // would be mismatch for non-container

        Assert.True(_comparer.MatchesSavedRights(entry, state, isContainer: true));
    }

    // --- AutoPopulateMissingSavedRights ---

    [Fact]
    public void AutoPopulate_AllowMode_NullSavedRights_PopulatesFromNtfsState()
    {
        var entry = new GrantedPathEntry { IsDeny = false, SavedRights = null };
        var state = AllowState(
            execute: CheckState.Checked,
            write: CheckState.Checked,
            special: CheckState.Unchecked,
            isAccountOwner: CheckState.Checked);

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
            denyRead: CheckState.Checked,
            denyExecute: CheckState.Checked,
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
        var state = AllowState(execute: CheckState.Unchecked);

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
        var state = AllowState(isAccountOwner: CheckState.Checked); // account is owner

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
}