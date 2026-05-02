namespace RunFence.Firewall.Wfp;

/// <summary>
/// Shared helper for adding and deleting WFP BLOCK filters.
/// Implemented by <see cref="WfpFilterHelperService"/>; mockable for testing.
/// </summary>
public interface IWfpFilterHelper
{
    /// <summary>
    /// Deletes the WFP filter identified by <paramref name="filterKey"/>.
    /// Logs a warning on unexpected failure; ignores <c>FWP_E_FILTER_NOT_FOUND</c>.
    /// </summary>
    void DeleteFilter(IntPtr engineHandle, ref Guid filterKey, string logPrefix);

    /// <summary>
    /// Converts <paramref name="sddl"/> to a security descriptor, allocates a condition array,
    /// invokes <paramref name="writeConditions"/> to populate it, builds the FWPM_FILTER0 struct,
    /// calls <c>FwpmFilterAdd0</c>, and frees all unmanaged memory.
    /// </summary>
    void AddFilterWithSddl(
        IntPtr engineHandle,
        string sddl,
        int conditionCount,
        ref Guid filterKey,
        ref Guid layerKey,
        string filterName,
        uint filterFlags,
        string logPrefix,
        Action<IntPtr, IntPtr, List<IntPtr>> writeConditions);

    /// <summary>
    /// Allocates a condition array of <paramref name="maxConditionCount"/> slots, invokes
    /// <paramref name="writeConditions"/> to populate it (returning the actual count written),
    /// builds the FWPM_FILTER0 struct using <paramref name="isIPv6"/> to select the layer key,
    /// calls <c>FwpmFilterAdd0</c>, and frees all unmanaged memory.
    /// No per-SID SDDL security descriptor is applied — the filter matches globally.
    /// When <paramref name="writeConditions"/> returns zero, the filter is skipped.
    /// </summary>
    void AddFilterGlobal(
        IntPtr engineHandle,
        int maxConditionCount,
        Guid filterKey,
        bool isIPv6,
        string filterName,
        string logPrefix,
        Func<IntPtr, List<IntPtr>, int> writeConditions);
}
