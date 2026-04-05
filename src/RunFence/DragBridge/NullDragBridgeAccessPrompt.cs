namespace RunFence.DragBridge;

/// <summary>No-op prompt that always cancels — used as fallback when no UI prompt is wired.</summary>
public sealed class NullDragBridgeAccessPrompt : IDragBridgeAccessPrompt
{
    public static readonly NullDragBridgeAccessPrompt Instance = new();

    private NullDragBridgeAccessPrompt()
    {
    }

    public DragBridgeAccessAction? Ask(string targetDisplayName, IReadOnlyList<string> inaccessiblePaths, long totalSizeBytes) => null;
}