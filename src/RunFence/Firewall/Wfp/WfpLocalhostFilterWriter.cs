namespace RunFence.Firewall.Wfp;

/// <summary>
/// Writes and deletes WFP BLOCK filters for per-user loopback blocking.
///
/// Each BLOCK filter has (2 + N) conditions (all must match):
/// (1) FWPM_CONDITION_FLAGS MATCH_FLAGS_ANY_SET FWP_CONDITION_FLAG_IS_LOOPBACK — loopback traffic only
/// (2) FWPM_CONDITION_ALE_USER_ID MATCH_EQUAL &lt;SD&gt; — specific user SID
/// (3..2+N) FWPM_CONDITION_IP_REMOTE_PORT MATCH_RANGE &lt;low,high&gt; — port ranges to block
///
/// Port exemptions use RANGE conditions because WFP ORs multiple conditions on the same field.
/// NOT_EQUAL(53) OR NOT_EQUAL(3000) is always TRUE, blocking all ports. Instead, we compute
/// contiguous port ranges that EXCLUDE the exempted ports/ranges:
///   exempted [53, 3000-3010] → blocked [1,52], [54,2999], [3011,65535]
/// A port falls in a blocked range → condition TRUE → block fires. An exempted port falls in no
/// blocked range → all range conditions FALSE → OR = FALSE → block doesn't fire → traffic allowed.
///
/// Static and ephemeral port ranges are written to separate WFP filters (V4/V6 and EV4/EV6)
/// to keep per-filter condition counts low. Both filter types use the same
/// <see cref="AddLocalhostFilter"/> method — callers pass pre-computed port ranges.
///
/// With 0 exempted entries, a single range [1, 65535] is used.
/// Maximum 16 exempted entries → maximum 17 blocked static ranges → maximum 19 conditions.
///
/// Separate filter objects are added for the V4 and V6 ALE_AUTH_CONNECT layers.
/// </summary>
internal sealed class WfpLocalhostFilterWriter(IWfpFilterHelper filterHelper)
{
    private const string LogPrefix = "WfpLocalhostFilterWriter";

    public void DeleteFilter(IntPtr handle, ref Guid key) =>
        filterHelper.DeleteFilter(handle, ref key, LogPrefix);

    public void AddLocalhostFilter(IntPtr handle, Guid filterKey, string sddl, bool isIPv6,
        IReadOnlyList<PortRange> blockedPortRanges, bool persistent = true)
    {
        var layerKey = isIPv6 ? WfpNative.LayerAleAuthConnectV6 : WfpNative.LayerAleAuthConnectV4;
        var conditionCount = 2 + blockedPortRanges.Count;
        uint filterFlags = persistent ? WfpNative.FWPM_FILTER_FLAG_PERSISTENT : 0;

        filterHelper.AddFilterWithSddl(
            handle, sddl, conditionCount, ref filterKey, ref layerKey,
            "RunFence Localhost Block", filterFlags, LogPrefix,
            (condArrayPtr, sdBlobPtr, marshalAllocs) =>
            {
                // Condition 0: loopback flag
                WfpFilterStructHelper.WriteConditionInline(condArrayPtr, 0,
                    WfpNative.ConditionFlags,
                    WfpNative.FWP_MATCH_FLAGS_ANY_SET,
                    WfpNative.FWP_UINT32, WfpNative.FWP_CONDITION_FLAG_IS_LOOPBACK);

                // Condition 1: user SID
                WfpFilterStructHelper.WriteCondition(condArrayPtr, 1,
                    WfpNative.ConditionAleUserId,
                    WfpNative.FWP_MATCH_EQUAL,
                    WfpNative.FWP_SECURITY_DESCRIPTOR_TYPE, sdBlobPtr);

                // Conditions 2..2+N: port ranges to block.
                // Same-field conditions are ORed by WFP, so the block fires when the packet's
                // destination port falls within ANY of these ranges.
                for (var i = 0; i < blockedPortRanges.Count; i++)
                {
                    WfpFilterStructHelper.WriteConditionRange(condArrayPtr, 2 + i,
                        WfpNative.ConditionIpRemotePort,
                        WfpNative.FWP_UINT16, (uint)blockedPortRanges[i].Low, (uint)blockedPortRanges[i].High,
                        marshalAllocs);
                }
            });
    }

    /// <summary>
    /// Given a list of exempted port ranges, returns contiguous [low,high] ranges covering ports
    /// 1–65535 that are NOT exempted. Overlapping/adjacent exempted ranges are merged.
    /// Empty list → single range [1, 65535].
    /// </summary>
    private const int MaxPort = 65535;

    internal static List<PortRange> BuildBlockedPortRanges(IReadOnlyList<PortRange> exemptedRanges)
    {
        // Clamp exempted ranges to [1, MaxPort]
        var clamped = exemptedRanges
            .Select(r => (Low: Math.Max(r.Low, 1), High: Math.Min(r.High, MaxPort)))
            .OrderBy(r => r.Low)
            .ToList();

        // Merge overlapping/adjacent exempted ranges
        var merged = new List<(int Low, int High)>();
        foreach (var r in clamped)
        {
            if (merged.Count > 0 && r.Low <= merged[^1].High + 1)
                merged[^1] = (merged[^1].Low, Math.Max(merged[^1].High, r.High));
            else
                merged.Add(r);
        }

        // Build complementary blocked ranges
        var blocked = new List<PortRange>();
        var next = 1;
        foreach (var (low, high) in merged)
        {
            if (low > next)
                blocked.Add(new PortRange(next, low - 1));
            next = high + 1;
        }

        if (next <= MaxPort)
            blocked.Add(new PortRange(next, MaxPort));

        return blocked;
    }
}
