using RunFence.Core;

namespace RunFence.Firewall.Wfp;

/// <summary>
/// Manages the WFP engine open/close and transaction begin/commit/abort lifecycle.
/// Shared by WfpLocalhostBlocker and WfpIcmpBlocker to avoid duplicating the boilerplate.
/// </summary>
internal sealed class WfpTransactionHelper(ILoggingService log)
{
    /// <summary>
    /// Opens a WFP engine, runs <paramref name="work"/> inside a transaction, and closes the engine.
    /// Returns <see langword="true"/> if the transaction committed successfully, <see langword="false"/> otherwise.
    /// Failures are logged; exceptions from <paramref name="work"/> are caught and logged.
    /// </summary>
    public bool ExecuteInTransaction(string callerName, Action<IntPtr> work)
    {
        try
        {
            var rc = WfpNative.FwpmEngineOpen0(null, WfpNative.RPC_C_AUTHN_DEFAULT,
                IntPtr.Zero, IntPtr.Zero, out var handle);
            if (rc != WfpNative.ERROR_SUCCESS)
            {
                log.Warn($"{callerName}: FwpmEngineOpen0 failed (0x{rc:X8})");
                return false;
            }
            try
            {
                return ExecuteOnHandle(callerName, handle, work);
            }
            finally
            {
                WfpNative.FwpmEngineClose0(handle);
            }
        }
        catch (Exception ex)
        {
            log.Error($"{callerName}: transaction failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a WFP transaction on a pre-opened engine handle.
    /// The caller is responsible for the handle's lifetime (open/close).
    /// Returns <see langword="true"/> if the transaction committed successfully, <see langword="false"/> otherwise.
    /// Failures are logged; exceptions from <paramref name="work"/> are caught and logged.
    /// </summary>
    public bool ExecuteOnHandle(string callerName, IntPtr handle, Action<IntPtr> work)
    {
        try
        {
            var rc = WfpNative.FwpmTransactionBegin0(handle, 0);
            if (rc != WfpNative.ERROR_SUCCESS)
            {
                log.Warn($"{callerName}: FwpmTransactionBegin0 failed (0x{rc:X8})");
                return false;
            }
            bool committed = false;
            try
            {
                work(handle);
                rc = WfpNative.FwpmTransactionCommit0(handle);
                if (rc != WfpNative.ERROR_SUCCESS)
                    log.Warn($"{callerName}: FwpmTransactionCommit0 failed (0x{rc:X8})");
                else
                    committed = true;
            }
            finally
            {
                if (!committed)
                    WfpNative.FwpmTransactionAbort0(handle);
            }
            return committed;
        }
        catch (Exception ex)
        {
            log.Error($"{callerName}: transaction failed", ex);
            return false;
        }
    }
}
