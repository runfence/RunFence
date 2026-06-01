using RunFence.Apps.Shortcuts;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class ShortcutProtectionOwnershipCalculatorTests
{
    private readonly ShortcutProtectionOwnershipCalculator _calculator = new();

    [Fact]
    public void BuildState_WithExternalReadOnlyAndDeny_DoesNotClaimOwnership()
    {
        var state = _calculator.BuildState(
            @"C:\shortcuts\app.lnk",
            existingState: null,
            wasReadOnlyBeforeProtection: true,
            hasOrdinaryManagedDenyAce: true,
            allowAdministratorsDelete: false);

        Assert.False(state.ManagedDenyAceApplied);
        Assert.False(state.ReadOnlySetByRunFence);
        Assert.True(state.WasReadOnlyBeforeProtection);
    }

    [Fact]
    public void BuildState_ExistingPersistedOwnership_PreservesOwnedFlags()
    {
        var existingState = new ShortcutProtectionState(@"C:\shortcuts\app.lnk", true, false, true);

        var state = _calculator.BuildState(
            existingState.ShortcutPath,
            existingState,
            wasReadOnlyBeforeProtection: true,
            hasOrdinaryManagedDenyAce: true,
            allowAdministratorsDelete: false);

        Assert.True(state.ManagedDenyAceApplied);
        Assert.True(state.ReadOnlySetByRunFence);
        Assert.False(state.WasReadOnlyBeforeProtection);
    }

    [Fact]
    public void BuildState_ExistingReadOnlyAndManagedDenyWithoutState_DoesNotRecoverOwnership()
    {
        var state = _calculator.BuildState(
            @"C:\shortcuts\app.lnk",
            existingState: null,
            wasReadOnlyBeforeProtection: true,
            hasOrdinaryManagedDenyAce: true,
            allowAdministratorsDelete: false);

        Assert.False(state.ManagedDenyAceApplied);
        Assert.False(state.ReadOnlySetByRunFence);
    }

    [Fact]
    public void BuildState_NoPersistedOwnership_DoesNotRecoverReadOnlyOwnership()
    {
        var state = _calculator.BuildState(
            @"C:\shortcuts\app.lnk",
            existingState: null,
            wasReadOnlyBeforeProtection: true,
            hasOrdinaryManagedDenyAce: false,
            allowAdministratorsDelete: true);

        Assert.False(state.ManagedDenyAceApplied);
        Assert.False(state.ReadOnlySetByRunFence);
    }

    [Fact]
    public void BuildState_AllowAdministratorsDelete_ClearsDenyOwnership()
    {
        var existingState = new ShortcutProtectionState(@"C:\shortcuts\app.lnk", true, false, true);

        var state = _calculator.BuildState(
            existingState.ShortcutPath,
            existingState,
            wasReadOnlyBeforeProtection: true,
            hasOrdinaryManagedDenyAce: true,
            allowAdministratorsDelete: true);

        Assert.False(state.ManagedDenyAceApplied);
        Assert.True(state.ReadOnlySetByRunFence);
    }
}
