namespace RunFence.Infrastructure;

public interface IUserHiveManager
{
    /// <summary>
    /// Ensures the user's registry hive is loaded in HKU.
    /// Returns IDisposable that unloads the hive on dispose, or null if already loaded.
    /// Returns null and logs warning on load failure.
    /// </summary>
    /// <remarks>
    /// Callers should ensure the returned disposable is disposed on a background thread.
    /// Its <see cref="IDisposable.Dispose"/> calls GC.Collect/WaitForPendingFinalizers
    /// to flush registry handles before unloading; this causes UI jank if called on the UI thread.
    /// </remarks>
    IDisposable? EnsureHiveLoaded(string sid);

    bool IsHiveLoaded(string sid);
}
