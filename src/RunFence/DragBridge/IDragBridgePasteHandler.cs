using System.Security.Principal;

namespace RunFence.DragBridge;

/// <summary>
/// Handles the paste side of the DragBridge flow: verifies file access, prompts about
/// inaccessible files, optionally grants ACLs or copies to temp, then returns the
/// resolved file paths.
/// </summary>
public interface IDragBridgePasteHandler
{
    Task<DragBridgeResolveResult> ResolveFileAccessAsync(
        SecurityIdentifier targetSid,
        List<string> filePaths,
        string sourceSid,
        IReadOnlyDictionary<string, string>? sidNames,
        CancellationToken ct);

    bool NeedsAccessResolution(SecurityIdentifier targetSid, IReadOnlyList<string> filePaths);
}
