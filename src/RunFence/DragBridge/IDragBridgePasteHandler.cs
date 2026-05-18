using System.Security.Principal;
using RunFence.Core.Models;

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
        SecurityIdentifier? targetContainerSid,
        List<string> filePaths,
        string sourceSid,
        string? sourceContainerSid,
        AppDatabase? database,
        CancellationToken ct);

    bool NeedsAccessResolution(SecurityIdentifier targetSid, SecurityIdentifier? targetContainerSid, IReadOnlyList<string> filePaths);
}
