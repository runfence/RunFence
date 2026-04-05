namespace RunFence.Infrastructure;

/// <summary>
/// Tracks modal dialog depth across all panels. Thread-safe.
/// </summary>
public interface IModalTracker
{
    void BeginModal();
    void EndModal();
    bool AnyModalOpen { get; }
}