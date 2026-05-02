using System.Net;
using System.Net.Sockets;
using RunFence.Core;

namespace RunFence.Firewall.Wfp;

/// <summary>
/// Manages two global (no user identity) WFP BLOCK filters at ALE_AUTH_CONNECT V4/V6
/// that block ICMP to specified internet address ranges.
///
/// Filter conditions: (IP_PROTOCOL = ICMP) AND (REMOTE_ADDR in CIDR_1 OR ... OR CIDR_N).
/// Same-field IP_REMOTE_ADDRESS conditions are ORed by WFP; IP_PROTOCOL is a different field,
/// so it is ANDed with the combined address condition.
///
/// Stable filter keys derived from a fixed "global" pseudo-SID ensure old filters are always
/// found and replaced, even across restarts with changed address lists.
/// </summary>
public sealed class WfpGlobalIcmpBlocker(ILoggingService log, IWfpFilterHelper filterHelper) : IWfpGlobalIcmpBlocker
{
    private static readonly Guid V4FilterKey = WfpFilterKeyHelper.DeriveKey("RunFence-GIC-V4", "global");
    private static readonly Guid V6FilterKey = WfpFilterKeyHelper.DeriveKey("RunFence-GIC-V6", "global");

    private readonly WfpTransactionHelper _txHelper = new(log);
    private readonly Lock _lock = new();

    public void Apply(IReadOnlyList<string> ipv4CidrRanges, IReadOnlyList<string> ipv6CidrRanges)
    {
        lock (_lock)
        {
            bool removing = ipv4CidrRanges.Count == 0 && ipv6CidrRanges.Count == 0;
            log.Info(removing
                ? "WfpGlobalIcmpBlocker: removing global ICMP block filters"
                : $"WfpGlobalIcmpBlocker: applying global ICMP block ({ipv4CidrRanges.Count} IPv4 + {ipv6CidrRanges.Count} IPv6 CIDR ranges)");

            _txHelper.ExecuteInTransaction("WfpGlobalIcmpBlocker", handle =>
            {
                var v4Key = V4FilterKey;
                var v6Key = V6FilterKey;
                filterHelper.DeleteFilter(handle, ref v4Key, "WfpGlobalIcmpBlocker");
                filterHelper.DeleteFilter(handle, ref v6Key, "WfpGlobalIcmpBlocker");

                if (ipv4CidrRanges.Count > 0)
                    AddGlobalFilter(handle, V4FilterKey, isIPv6: false, WfpNative.IPPROTO_ICMP, ipv4CidrRanges);
                if (ipv6CidrRanges.Count > 0)
                    AddGlobalFilter(handle, V6FilterKey, isIPv6: true, WfpNative.IPPROTO_ICMPV6, ipv6CidrRanges);
            });
        }
    }

    private void AddGlobalFilter(IntPtr handle, Guid filterKey, bool isIPv6, uint protocol,
        IReadOnlyList<string> cidrRanges)
    {
        // Pre-allocate condition slots: 1 protocol condition + up to N address conditions.
        // Unused trailing slots remain zeroed; WFP reads only actualCondCount entries.
        int maxConds = 1 + cidrRanges.Count;

        filterHelper.AddFilterGlobal(handle, maxConds, filterKey, isIPv6,
            "RunFence Global ICMP Block", "WfpGlobalIcmpBlocker",
            (condArrayPtr, marshalAllocs) =>
            {
                // Condition 0: IP_PROTOCOL = ICMP or ICMPv6 (different field → ANDed with address conditions)
                WfpFilterStructHelper.WriteConditionInline(condArrayPtr, 0,
                    WfpNative.ConditionIpProtocol,
                    WfpNative.FWP_MATCH_EQUAL,
                    WfpNative.FWP_UINT8, protocol);

                // Conditions 1..N: remote address CIDRs (same field → ORed by WFP)
                int condIdx = 1;
                foreach (var cidr in cidrRanges)
                {
                    if (isIPv6)
                    {
                        if (TryParseIPv6Cidr(cidr, out var addrBytes, out var prefix))
                            WfpFilterStructHelper.WriteConditionV6Subnet(condArrayPtr, condIdx++, addrBytes, prefix, marshalAllocs);
                    }
                    else
                    {
                        if (TryParseIPv4Cidr(cidr, out var addrBe, out var maskBe))
                            WfpFilterStructHelper.WriteConditionV4Subnet(condArrayPtr, condIdx++, addrBe, maskBe, marshalAllocs);
                    }
                }

                // Return 0 when no valid CIDRs were written (protocol-only filter would block all ICMP, not just to specific ranges)
                return condIdx > 1 ? condIdx : 0;
            });
    }

    private static bool TryParseIPv4Cidr(string cidr, out uint addrBe, out uint maskBe)
    {
        addrBe = 0;
        maskBe = 0;
        var slash = cidr.LastIndexOf('/');
        if (slash < 0 || !IPAddress.TryParse(cidr[..slash], out var addr))
            return false;
        if (addr.AddressFamily != AddressFamily.InterNetwork)
            return false;
        if (!int.TryParse(cidr[(slash + 1)..], out var prefix) || prefix < 0 || prefix > 32)
            return false;
        var bytes = addr.GetAddressBytes();
        addrBe = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        maskBe = prefix == 0 ? 0u : ~((1u << (32 - prefix)) - 1u);
        return true;
    }

    private static bool TryParseIPv6Cidr(string cidr, out byte[] addrBytes, out byte prefix)
    {
        addrBytes = [];
        prefix = 0;
        var slash = cidr.LastIndexOf('/');
        if (slash < 0 || !IPAddress.TryParse(cidr[..slash], out var addr))
            return false;
        if (addr.AddressFamily != AddressFamily.InterNetworkV6)
            return false;
        if (!int.TryParse(cidr[(slash + 1)..], out var plen) || plen < 0 || plen > 128)
            return false;
        addrBytes = addr.GetAddressBytes();
        prefix = (byte)plen;
        return true;
    }
}
