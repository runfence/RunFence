using RunFence.Core;
using RunFence.Firewall;

namespace RunFence.Firewall.Wfp;

/// <summary>
/// Manages WFP BLOCK filters for per-user raw-socket ICMP at ALE_AUTH_CONNECT.
/// Blocks ICMP sent via raw sockets (SOCK_RAW) from elevated/admin processes that have
/// per-user identity at ALE. IcmpSendEcho2 (non-admin path) carries SYSTEM identity and
/// is blocked globally by <see cref="IWfpGlobalIcmpBlocker"/> instead.
/// Filter keys are derived deterministically from the SID.
/// </summary>
public sealed class WfpIcmpBlocker(ILoggingService log, IWfpFilterHelper filterHelper) : IWfpIcmpBlocker
{
    private readonly WfpIcmpFilterWriter _filterWriter = new(filterHelper);
    private readonly WfpTransactionHelper _txHelper = new(log);

    public void Apply(string sid, bool block)
    {
        _txHelper.ExecuteInTransaction("WfpIcmpBlocker", handle =>
        {
            var v4Key = WfpFilterKeyHelper.DeriveKey("RunFence-IC-V4", sid);
            var v6Key = WfpFilterKeyHelper.DeriveKey("RunFence-IC-V6", sid);
            // Also delete legacy diagnostic RECV_ACCEPT keys that may have been installed
            // by a previous version of RunFence; they are no longer created.
            var rv4Key = WfpFilterKeyHelper.DeriveKey("RunFence-IC-RV4", sid);
            var rv6Key = WfpFilterKeyHelper.DeriveKey("RunFence-IC-RV6", sid);
            _filterWriter.DeleteFilter(handle, ref v4Key);
            _filterWriter.DeleteFilter(handle, ref v6Key);
            _filterWriter.DeleteFilter(handle, ref rv4Key);
            _filterWriter.DeleteFilter(handle, ref rv6Key);
            if (block)
            {
                var sddl = FirewallSddlHelper.BuildSddl(sid);
                _filterWriter.AddIcmpBlockFilter(handle, v4Key, sddl, WfpNative.LayerAleAuthConnectV4, WfpNative.IPPROTO_ICMP);
                _filterWriter.AddIcmpBlockFilter(handle, v6Key, sddl, WfpNative.LayerAleAuthConnectV6, WfpNative.IPPROTO_ICMPV6);
            }
        });
    }
}
