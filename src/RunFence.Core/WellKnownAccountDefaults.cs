using RunFence.Core.Models;

namespace RunFence.Core;

/// <summary>
/// Applies privilege-level defaults for well-known Windows accounts whose privilege level
/// is an OS invariant (e.g. SYSTEM always runs at highest allowed), not a user-configurable choice.
/// </summary>
public static class WellKnownAccountDefaults
{
    public static void Apply(AppDatabase database)
    {
        var system = database.GetOrCreateAccount(SidConstants.SystemSid);
        system.PrivilegeLevel = PrivilegeLevel.HighestAllowed;
        system.ManageAssociations = false;
    }
}
