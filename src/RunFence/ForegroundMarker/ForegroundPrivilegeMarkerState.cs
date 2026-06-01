using System.Drawing;

namespace RunFence.ForegroundMarker;

public sealed record class ForegroundPrivilegeMarkerState
{

    public bool IsActive { get; }

    public ForegroundPrivilegeMarkerKind? Kind { get; }

    public ForegroundPrivilegeTooltipMode? TooltipMode { get; }

    public Color? Color { get; }

    public ForegroundPrivilegeMarkerMetadata? Metadata { get; }

    private ForegroundPrivilegeMarkerState(
        bool isActive,
        ForegroundPrivilegeMarkerKind? kind,
        ForegroundPrivilegeTooltipMode? tooltipMode,
        Color? color,
        ForegroundPrivilegeMarkerMetadata? metadata)
    {
        IsActive = isActive;
        Kind = kind;
        TooltipMode = tooltipMode;
        Color = color;
        Metadata = metadata;
    }

    public static ForegroundPrivilegeMarkerState Inactive { get; } = new(false, null, null, null, null);

    public static ForegroundPrivilegeMarkerState Active(
        ForegroundPrivilegeMarkerKind kind,
        Color color,
        ForegroundPrivilegeMarkerMetadata metadata,
        ForegroundPrivilegeTooltipMode? tooltipMode = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new ForegroundPrivilegeMarkerState(
            true,
            kind,
            tooltipMode ?? (kind switch
            {
                ForegroundPrivilegeMarkerKind.Basic => null,
                ForegroundPrivilegeMarkerKind.Isolated => ForegroundPrivilegeTooltipMode.Isolated,
                ForegroundPrivilegeMarkerKind.LowIL => ForegroundPrivilegeTooltipMode.LowIL,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            }),
            color,
            metadata);
    }

    public static ForegroundPrivilegeMarkerState TooltipOnly(
        ForegroundPrivilegeMarkerMetadata metadata,
        ForegroundPrivilegeMarkerKind? kind = null,
        ForegroundPrivilegeTooltipMode? tooltipMode = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new ForegroundPrivilegeMarkerState(false, kind, tooltipMode, null, metadata);
    }
}
