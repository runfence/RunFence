namespace RunFence.DragBridge;

/// <summary>
/// Composed interface combining <see cref="IDragBridgeSessionManager"/> and <see cref="IDragBridgePipeLauncher"/>.
/// The concrete <see cref="DragBridgeProcessLauncher"/> implements both.
/// </summary>
public interface IDragBridgeProcessLauncher : IDragBridgeSessionManager, IDragBridgePipeLauncher;
