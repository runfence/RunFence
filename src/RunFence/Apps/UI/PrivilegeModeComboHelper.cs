using RunFence.Core.Models;

namespace RunFence.Apps.UI;

/// <summary>
/// Conversion helpers between the PrivilegeLevel combobox index and <see cref="PrivilegeLevel"/> value.
/// The combobox items are ordered for UX clarity (not enum value order):
/// index 0 = null (Account default), 1 = HighestAllowed, 2 = Basic, 3 = LowIntegrity.
/// </summary>
public static class PrivilegeLevelComboHelper
{
    /// <summary>
    /// Converts a combobox index to a nullable <see cref="PrivilegeLevel"/>.
    /// Index 0 maps to null (account default); indices 1-3 map to the corresponding mode.
    /// </summary>
    public static PrivilegeLevel? IndexToMode(int index) => index switch
    {
        1 => PrivilegeLevel.HighestAllowed,
        2 => PrivilegeLevel.Basic,
        3 => PrivilegeLevel.LowIntegrity,
        _ => null
    };

    /// <summary>
    /// Converts a nullable <see cref="PrivilegeLevel"/> to the corresponding combobox index.
    /// Null maps to index 0 (account default).
    /// </summary>
    public static int ModeToIndex(PrivilegeLevel? mode) => mode switch
    {
        PrivilegeLevel.HighestAllowed => 1,
        PrivilegeLevel.Basic => 2,
        PrivilegeLevel.LowIntegrity => 3,
        _ => 0
    };
}
