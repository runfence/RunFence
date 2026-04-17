using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Firewall.Wfp;

/// <summary>
/// Implements <see cref="IWfpFilterHelper"/> with P/Invoke calls to WFP APIs.
/// Replaces the former static helper class; log is injected via constructor.
/// </summary>
public class WfpFilterHelperService(ILoggingService log) : IWfpFilterHelper
{
    public void DeleteFilter(IntPtr engineHandle, ref Guid filterKey, string logPrefix)
    {
        var rc = WfpNative.FwpmFilterDeleteByKey0(engineHandle, ref filterKey);
        if (rc != WfpNative.ERROR_SUCCESS && rc != WfpNative.FWP_E_FILTER_NOT_FOUND)
            log.Warn($"{logPrefix}: FwpmFilterDeleteByKey0 failed (0x{rc:X8})");
    }

    public void AddFilterWithSddl(
        IntPtr engineHandle,
        string sddl,
        int conditionCount,
        ref Guid filterKey,
        ref Guid layerKey,
        string filterName,
        uint filterFlags,
        string logPrefix,
        Action<IntPtr, IntPtr, List<IntPtr>> writeConditions)
    {
        var marshalAllocs = new List<IntPtr>();
        IntPtr sdPtr = IntPtr.Zero;
        try
        {
            if (!WfpNative.ConvertStringSecurityDescriptorToSecurityDescriptor(
                    sddl, WfpNative.SDDL_REVISION_1, out sdPtr, out uint sdSize))
            {
                log.Warn($"{logPrefix}: Failed to convert SDDL (error {Marshal.GetLastWin32Error()})");
                return;
            }

            var sdBlobPtr = WfpFilterStructHelper.CreateSdBlob(sdPtr, sdSize, marshalAllocs);

            var condArrayPtr = Marshal.AllocHGlobal(conditionCount * 40);
            marshalAllocs.Add(condArrayPtr);
            WfpFilterStructHelper.ZeroMemory(condArrayPtr, conditionCount * 40);

            writeConditions(condArrayPtr, sdBlobPtr, marshalAllocs);

            var filterPtr = Marshal.AllocHGlobal(200);
            marshalAllocs.Add(filterPtr);
            WfpFilterStructHelper.ZeroMemory(filterPtr, 200);
            var namePtr = Marshal.StringToHGlobalUni(filterName);
            marshalAllocs.Add(namePtr);

            WfpFilterStructHelper.WriteFilter(filterPtr, filterKey, layerKey,
                condArrayPtr, (uint)conditionCount, namePtr, WfpNative.FWP_ACTION_BLOCK, filterFlags);

            var addRc = WfpNative.FwpmFilterAdd0(engineHandle, filterPtr, IntPtr.Zero, out _);
            if (addRc != WfpNative.ERROR_SUCCESS)
                log.Warn($"{logPrefix}: FwpmFilterAdd0 failed (0x{addRc:X8})");
        }
        finally
        {
            foreach (var ptr in marshalAllocs)
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }

            if (sdPtr != IntPtr.Zero)
                ProcessNative.LocalFree(sdPtr);
        }
    }
}
