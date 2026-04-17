using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
            if (removing)
                log.Info("WfpGlobalIcmpBlocker: removing global ICMP block filters");
            else
                log.Info($"WfpGlobalIcmpBlocker: applying global ICMP block ({ipv4CidrRanges.Count} IPv4 + {ipv6CidrRanges.Count} IPv6 CIDR ranges)");

            _txHelper.ExecuteInTransaction("WfpGlobalIcmpBlocker", handle =>
            {
                var v4Key = V4FilterKey;
                var v6Key = V6FilterKey;
                DeleteFilter(handle, ref v4Key);
                DeleteFilter(handle, ref v6Key);

                if (ipv4CidrRanges.Count > 0)
                    AddGlobalFilter(handle, V4FilterKey, isIPv6: false, WfpNative.IPPROTO_ICMP, ipv4CidrRanges);
                if (ipv6CidrRanges.Count > 0)
                    AddGlobalFilter(handle, V6FilterKey, isIPv6: true, WfpNative.IPPROTO_ICMPV6, ipv6CidrRanges);
            });
        }
    }

    private void DeleteFilter(IntPtr handle, ref Guid key) =>
        filterHelper.DeleteFilter(handle, ref key, "WfpGlobalIcmpBlocker");

    private void AddGlobalFilter(IntPtr handle, Guid filterKey, bool isIPv6, uint protocol,
        IReadOnlyList<string> cidrRanges)
    {
        var marshalAllocs = new List<IntPtr>();
        try
        {
            // Pre-allocate condition array: 1 protocol condition + up to N address conditions.
            // Unused trailing slots remain zeroed; WFP reads only actualCondCount entries.
            int maxConds = 1 + cidrRanges.Count;
            var condArrayPtr = Marshal.AllocHGlobal(maxConds * 40);
            marshalAllocs.Add(condArrayPtr);
            WfpFilterStructHelper.ZeroMemory(condArrayPtr, maxConds * 40);

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

            if (condIdx == 1)
                return; // All CIDRs were invalid; skip filter

            var filterPtr = Marshal.AllocHGlobal(200);
            marshalAllocs.Add(filterPtr);
            WfpFilterStructHelper.ZeroMemory(filterPtr, 200);
            var namePtr = Marshal.StringToHGlobalUni("RunFence Global ICMP Block");
            marshalAllocs.Add(namePtr);
            WfpFilterStructHelper.WriteFilter(filterPtr, filterKey, isIPv6, condArrayPtr,
                (uint)condIdx, namePtr, WfpNative.FWP_ACTION_BLOCK);

            var addRc = WfpNative.FwpmFilterAdd0(handle, filterPtr, IntPtr.Zero, out _);
            if (addRc != WfpNative.ERROR_SUCCESS)
                log.Warn($"WfpGlobalIcmpBlocker: FwpmFilterAdd0 failed (0x{addRc:X8})");
        }
        finally
        {
            foreach (var ptr in marshalAllocs)
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }
        }
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
