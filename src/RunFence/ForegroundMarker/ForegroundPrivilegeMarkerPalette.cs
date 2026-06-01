using System.Drawing;

namespace RunFence.ForegroundMarker;

public static class ForegroundPrivilegeMarkerPalette
{
    public static Color Basic { get; } = Color.FromArgb(0, 120, 215);
    public static Color Isolated { get; } = Color.FromArgb(245, 124, 0);
    public static Color LowIntegrity { get; } = Color.FromArgb(196, 0, 122);

    public static Color GetColor(ForegroundPrivilegeMarkerKind kind) =>
        kind switch
        {
            ForegroundPrivilegeMarkerKind.Basic => Basic,
            ForegroundPrivilegeMarkerKind.Isolated => Isolated,
            ForegroundPrivilegeMarkerKind.LowIL => LowIntegrity,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
