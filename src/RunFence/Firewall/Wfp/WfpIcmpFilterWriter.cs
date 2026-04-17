namespace RunFence.Firewall.Wfp;

/// <summary>
/// Writes and deletes WFP BLOCK filters for per-user raw-socket ICMP blocking at ALE layers.
///
/// Each filter has 2 conditions (all must match for BLOCK action):
/// (1) FWPM_CONDITION_ALE_USER_ID MATCH_EQUAL &lt;SD&gt; — specific user SID
/// (2) FWPM_CONDITION_IP_PROTOCOL MATCH_EQUAL &lt;IPPROTO_ICMP|IPPROTO_ICMPV6&gt; — ICMP protocol
///
/// These filters block ICMP sent via raw sockets (SOCK_RAW) at FWPM_LAYER_ALE_AUTH_CONNECT_V4/V6.
/// IcmpSendEcho2 (non-admin ping) bypasses ALE — blocked globally by <see cref="IWfpGlobalIcmpBlocker"/>.
/// </summary>
internal sealed class WfpIcmpFilterWriter(IWfpFilterHelper filterHelper)
{
    private const string LogPrefix = "WfpIcmpFilterWriter";

    public void DeleteFilter(IntPtr handle, ref Guid key) =>
        filterHelper.DeleteFilter(handle, ref key, LogPrefix);

    public void AddIcmpBlockFilter(IntPtr handle, Guid filterKey, string sddl, Guid layerKey, uint protocol)
    {
        filterHelper.AddFilterWithSddl(
            handle, sddl, conditionCount: 2, ref filterKey, ref layerKey,
            "RunFence ICMP Block", WfpNative.FWPM_FILTER_FLAG_PERSISTENT, LogPrefix,
            (condArrayPtr, sdBlobPtr, _) =>
            {
                // Condition 0: user SID
                WfpFilterStructHelper.WriteCondition(condArrayPtr, 0,
                    WfpNative.ConditionAleUserId,
                    WfpNative.FWP_MATCH_EQUAL,
                    WfpNative.FWP_SECURITY_DESCRIPTOR_TYPE, sdBlobPtr);

                // Condition 1: ICMP protocol — FWP_UINT8 stored inline in union.
                // IPPROTO_ICMP=1 for IPv4, IPPROTO_ICMPV6=58 for IPv6.
                WfpFilterStructHelper.WriteConditionInline(condArrayPtr, 1,
                    WfpNative.ConditionIpProtocol,
                    WfpNative.FWP_MATCH_EQUAL,
                    WfpNative.FWP_UINT8, protocol);
            });
    }
}
