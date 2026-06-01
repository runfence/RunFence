using System.Drawing;

namespace RunFence.ForegroundMarker;

public readonly record struct ForegroundMarkerPlacement(Rectangle WindowBounds, bool RenderInsideLeftEdge);
