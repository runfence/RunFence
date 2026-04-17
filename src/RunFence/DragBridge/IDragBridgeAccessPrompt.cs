namespace RunFence.DragBridge;

public enum DragBridgeAccessAction
{
    GrantAccess,
    GrantFolderAccess,
    CopyToTemp
}

/// <summary>
/// Prompts the user to choose how to handle files inaccessible by the paste target account.
/// Returns the chosen action, or null if the user cancelled.
/// </summary>
public interface IDragBridgeAccessPrompt
{
    DragBridgeAccessAction? Ask(string targetDisplayName, IReadOnlyList<string> inaccessiblePaths, long totalSizeBytes);
}