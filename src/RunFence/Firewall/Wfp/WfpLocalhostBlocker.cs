using System.Security.Cryptography;
using System.Text;
using RunFence.Core;

namespace RunFence.Firewall.Wfp;

/// <summary>
/// Manages persistent WFP filters for per-user loopback blocking.
/// Windows Firewall (INetFwRule) implicitly excludes loopback traffic, so direct WFP filters at
/// FWPM_LAYER_ALE_AUTH_CONNECT_V4/V6 are required to correctly block loopback for a specified user SID.
/// Filter keys are derived deterministically from the SID so filters can be found and deleted without
/// storing IDs in the database.
/// </summary>
public sealed class WfpLocalhostBlocker : IWfpLocalhostBlocker
{
    private readonly ILoggingService _log;
    private readonly WfpLocalhostFilterWriter _filterWriter;

    public WfpLocalhostBlocker(ILoggingService log)
    {
        _log = log;
        _filterWriter = new WfpLocalhostFilterWriter(log);
    }

    public void Apply(string sid, bool block)
    {
        try
        {
            var rc = WfpNative.FwpmEngineOpen0(null, WfpNative.RPC_C_AUTHN_DEFAULT,
                IntPtr.Zero, IntPtr.Zero, out var handle);
            if (rc != WfpNative.ERROR_SUCCESS)
            {
                _log.Warn($"WfpLocalhostBlocker: FwpmEngineOpen0 failed (0x{rc:X8})");
                return;
            }

            try
            {
                rc = WfpNative.FwpmTransactionBegin0(handle, 0);
                if (rc != WfpNative.ERROR_SUCCESS)
                {
                    _log.Warn($"WfpLocalhostBlocker: FwpmTransactionBegin0 failed (0x{rc:X8})");
                    return;
                }

                bool committed = false;
                try
                {
                    var v4Key = DeriveFilterKey("RunFence-LH-V4", sid);
                    var v6Key = DeriveFilterKey("RunFence-LH-V6", sid);
                    _filterWriter.DeleteFilter(handle, ref v4Key);
                    _filterWriter.DeleteFilter(handle, ref v6Key);
                    if (block)
                    {
                        var sddl = $"D:(A;;CC;;;{sid})";
                        _filterWriter.AddLocalhostFilter(handle, v4Key, sddl, isIPv6: false);
                        _filterWriter.AddLocalhostFilter(handle, v6Key, sddl, isIPv6: true);
                    }

                    rc = WfpNative.FwpmTransactionCommit0(handle);
                    if (rc != WfpNative.ERROR_SUCCESS)
                        _log.Warn($"WfpLocalhostBlocker: FwpmTransactionCommit0 failed (0x{rc:X8})");
                    else
                        committed = true;
                }
                finally
                {
                    if (!committed)
                        WfpNative.FwpmTransactionAbort0(handle);
                }
            }
            finally
            {
                WfpNative.FwpmEngineClose0(handle);
            }
        }
        catch (Exception ex)
        {
            _log.Error("WfpLocalhostBlocker: Apply failed", ex);
        }
    }

    /// <summary>
    /// Derives a deterministic GUID from a namespace prefix and account SID so that
    /// filters can be found and deleted without storing IDs in the database.
    /// </summary>
    private static Guid DeriveFilterKey(string prefix, string sid)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(prefix + sid));
        return new Guid(hash[..16]);
    }
}