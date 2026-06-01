using RunFence.Core.Models;

namespace RunFence.UI;

/// <summary>
/// Conversion helpers between the PrivilegeLevel combobox index and <see cref="PrivilegeLevel"/> value.
/// The combobox items are ordered for UX clarity (not enum value order):
/// index 0 = null (Account default), 1 = HighestAllowed, 2 = HighIntegrity, 3 = Basic, 4 = Isolated, 5 = LowIntegrity.
/// </summary>
public static class PrivilegeLevelComboHelper
{
    public static IReadOnlyList<PrivilegeLevel> OrderedModes { get; } =
    [
        PrivilegeLevel.HighestAllowed,
        PrivilegeLevel.HighIntegrity,
        PrivilegeLevel.Basic,
        PrivilegeLevel.Isolated,
        PrivilegeLevel.LowIntegrity
    ];

    /// <summary>
    /// Converts a combobox index to a nullable <see cref="PrivilegeLevel"/>.
    /// Index 0 maps to null (account default); indices 1-5 map to the corresponding mode.
    /// </summary>
    public static PrivilegeLevel? IndexToMode(int index) => index switch
    {
        1 => PrivilegeLevel.HighestAllowed,
        2 => PrivilegeLevel.HighIntegrity,
        3 => PrivilegeLevel.Basic,
        4 => PrivilegeLevel.Isolated,
        5 => PrivilegeLevel.LowIntegrity,
        _ => null
    };

    /// <summary>
    /// Converts a nullable <see cref="PrivilegeLevel"/> to the corresponding combobox index.
    /// Null maps to index 0 (account default).
    /// </summary>
    public static int ModeToIndex(PrivilegeLevel? mode) => mode switch
    {
        PrivilegeLevel.HighestAllowed => 1,
        PrivilegeLevel.HighIntegrity => 2,
        PrivilegeLevel.Basic => 3,
        PrivilegeLevel.Isolated => 4,
        PrivilegeLevel.LowIntegrity => 5,
        _ => 0
    };

    public static string GetDisplayText(PrivilegeLevel mode) => mode switch
    {
        PrivilegeLevel.HighestAllowed => "Highest Allowed",
        PrivilegeLevel.HighIntegrity => "High Integrity",
        PrivilegeLevel.Basic => "Basic",
        PrivilegeLevel.Isolated => "Isolated",
        PrivilegeLevel.LowIntegrity => "Low Integrity",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}
