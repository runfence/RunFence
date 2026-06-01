using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public class ShortcutProtectionOwnershipCalculator
{
    public ShortcutProtectionState BuildState(
        string shortcutPath,
        ShortcutProtectionState? existingState,
        bool wasReadOnlyBeforeProtection,
        bool hasOrdinaryManagedDenyAce,
        bool allowAdministratorsDelete)
    {
        var readOnlySetByRunFence = existingState?.ReadOnlySetByRunFence == true ||
                                    !wasReadOnlyBeforeProtection;

        var managedDenyAceApplied = !allowAdministratorsDelete &&
                                    (existingState?.ManagedDenyAceApplied == true ||
                                     !hasOrdinaryManagedDenyAce);

        return new ShortcutProtectionState(
            shortcutPath,
            managedDenyAceApplied,
            existingState?.WasReadOnlyBeforeProtection ?? wasReadOnlyBeforeProtection,
            readOnlySetByRunFence);
    }
}
