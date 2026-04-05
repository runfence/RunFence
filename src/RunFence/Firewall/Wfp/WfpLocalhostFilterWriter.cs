using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Firewall.Wfp;

/// <summary>
/// Writes and deletes WFP filters for per-user loopback blocking.
///
/// Three conditions per filter (all must match for BLOCK action):
/// (1) FWPM_CONDITION_FLAGS MATCH_FLAGS_ANY_SET FWP_CONDITION_FLAG_IS_LOOPBACK — loopback traffic only
/// (2) FWPM_CONDITION_ALE_USER_ID MATCH_EQUAL &lt;SD&gt; — specific user SID
/// (3) FWPM_CONDITION_IP_REMOTE_PORT NOT_EQUAL 53 — exempt DNS so local DNS resolvers (VPN, WSL2, etc.) still work
/// Separate filter objects are added for the V4 and V6 ALE_AUTH_CONNECT layers.
///
/// Binary layout constants (64-bit, verified against Windows SDK fwpmtypes.h):
///
///   FWPM_FILTER_CONDITION0 = 40 bytes
///     offset  0: GUID  fieldKey          (16)
///     offset 16: UINT32 matchType         (4)
///     offset 20: [pad 4]
///     offset 24: FWP_CONDITION_VALUE0     (16)
///       offset 24: UINT32 type            (4)
///       offset 28: [pad 4]
///       offset 32: union (8) — pointer for heap types; inline value for FWP_UINT8/16/32
///
///   FWP_BYTE_BLOB = 16 bytes
///     offset  0: UINT32 size             (4)
///     offset  4: [pad 4]
///     offset  8: IntPtr data             (8)
///
///   FWP_VALUE0 = 16 bytes
///     offset  0: UINT32 type             (4)
///     offset  4: [pad 4]
///     offset  8: value (union)           (8)
///
///   FWPM_ACTION0 = 20 bytes
///     offset  0: UINT32 type             (4)
///     offset  4: GUID (union)            (16)  ← providerContextKey or filterType
///
///   FWPM_FILTER0 = 200 bytes
///     offset   0: GUID  filterKey        (16)
///     offset  16: LPWSTR name            (8)   ← displayData.name
///     offset  24: LPWSTR desc            (8)   ← displayData.description
///     offset  32: UINT32 flags           (4)
///     offset  36: [pad 4]
///     offset  40: IntPtr providerKey     (8)
///     offset  48: UINT32 provDataSize    (4)   ← providerData.size
///     offset  52: [pad 4]
///     offset  56: IntPtr provDataData    (8)   ← providerData.data
///     offset  64: GUID  layerKey         (16)
///     offset  80: GUID  subLayerKey      (16)
///     offset  96: UINT32 weightType      (4)   ← weight.type = FWP_EMPTY (0 = auto-assign)
///     offset 100: [pad 4]
///     offset 104: UINT64 weightVal       (8)   ← weight union: NOTE FWP_UINT64 stores POINTER, not inline value; use FWP_EMPTY
///     offset 112: UINT32 numConditions   (4)
///     offset 116: [pad 4]
///     offset 120: IntPtr filterCond      (8)
///     offset 128: UINT32 actionType      (4)   ← action.type
///     offset 132: GUID  actionCtx        (16)  ← action.providerContextKey
///     offset 148: [pad 4]
///     offset 152: UINT64 rawContext      (8)
///     offset 160: [pad 8]  ← union between rawContext(8) and providerContextKey(16)
///     offset 168: IntPtr reserved        (8)
///     offset 176: UINT64 filterId        (8)   ← output
///     offset 184: FWP_VALUE0 effWeight   (16)  ← output
///     total = 200 bytes
/// </summary>
internal sealed class WfpLocalhostFilterWriter
{
    private readonly ILoggingService _log;

    public WfpLocalhostFilterWriter(ILoggingService log) => _log = log;

    public void DeleteFilter(IntPtr handle, ref Guid key)
    {
        var rc = WfpNative.FwpmFilterDeleteByKey0(handle, ref key);
        if (rc != WfpNative.ERROR_SUCCESS && rc != WfpNative.FWP_E_FILTER_NOT_FOUND)
            _log.Warn($"WfpLocalhostFilterWriter: FwpmFilterDeleteByKey0 failed (0x{rc:X8})");
    }

    public void AddLocalhostFilter(IntPtr handle, Guid filterKey, string sddl, bool isIPv6)
    {
        // Allocations that need Marshal.FreeHGlobal
        var marshalAllocs = new List<IntPtr>();
        // The security descriptor is allocated by LocalAlloc (via advapi32) and needs LocalFree
        IntPtr sdPtr = IntPtr.Zero;
        try
        {
            if (!WfpNative.ConvertStringSecurityDescriptorToSecurityDescriptor(
                    sddl, WfpNative.SDDL_REVISION_1, out sdPtr, out uint sdSize))
            {
                _log.Warn($"WfpLocalhostFilterWriter: Failed to convert SDDL (error {Marshal.GetLastWin32Error()})");
                return;
            }

            // FWP_BYTE_BLOB for the security descriptor (16 bytes: size@0 + [pad4] + data@8)
            var sdBlobPtr = Marshal.AllocHGlobal(16);
            marshalAllocs.Add(sdBlobPtr);
            ZeroMemory(sdBlobPtr, 16);
            Marshal.WriteInt32(sdBlobPtr, 0, (int)sdSize);
            Marshal.WriteIntPtr(sdBlobPtr, 8, sdPtr);

            // FWPM_FILTER_CONDITION0 array: 3 conditions × 40 bytes = 120 bytes
            var condArrayPtr = Marshal.AllocHGlobal(120);
            marshalAllocs.Add(condArrayPtr);
            ZeroMemory(condArrayPtr, 120);

            // Condition 0: loopback flag — FWP_UINT32 stored inline in union; no pointer needed.
            // FWP_CONDITION_FLAG_IS_LOOPBACK is valid at FWPM_LAYER_ALE_AUTH_CONNECT_V{4|6} and
            // matches all loopback traffic regardless of specific address (127.x.x.x, ::1, etc.).
            WriteConditionInline(condArrayPtr, 0,
                WfpNative.ConditionFlags,
                WfpNative.FWP_MATCH_FLAGS_ANY_SET,
                WfpNative.FWP_UINT32, WfpNative.FWP_CONDITION_FLAG_IS_LOOPBACK);

            // Condition 1: user SID (security descriptor)
            WriteCondition(condArrayPtr, 1,
                WfpNative.ConditionAleUserId,
                WfpNative.FWP_MATCH_EQUAL,
                WfpNative.FWP_SECURITY_DESCRIPTOR_TYPE, sdBlobPtr);

            // Condition 2: exempt DNS (port 53 TCP/UDP) from loopback blocking.
            // On machines with a local DNS resolver (VPN software, WSL2, Windows DoH proxy, etc.),
            // DNS queries go to 127.0.0.1:53 or ::1:53. Without this exemption, name resolution
            // breaks and internet stops working even though non-loopback routes are not blocked.
            WriteConditionInline(condArrayPtr, 2,
                WfpNative.ConditionIpRemotePort,
                WfpNative.FWP_MATCH_NOT_EQUAL,
                WfpNative.FWP_UINT16, WfpNative.DnsPort);

            // FWPM_FILTER0: 200 bytes
            var filterPtr = Marshal.AllocHGlobal(200);
            marshalAllocs.Add(filterPtr);
            ZeroMemory(filterPtr, 200);
            var namePtr = Marshal.StringToHGlobalUni("RunFence Localhost Block");
            marshalAllocs.Add(namePtr);
            WriteFilter(filterPtr, filterKey, isIPv6, condArrayPtr, 3, namePtr);

            var addRc = WfpNative.FwpmFilterAdd0(handle, filterPtr, IntPtr.Zero, out _);
            if (addRc != WfpNative.ERROR_SUCCESS)
                _log.Warn($"WfpLocalhostFilterWriter: FwpmFilterAdd0 failed (0x{addRc:X8})");
        }
        finally
        {
            // WFP makes a deep copy on FwpmFilterAdd0, so we can free immediately after.
            foreach (var ptr in marshalAllocs)
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }

            if (sdPtr != IntPtr.Zero)
                NativeMethods.LocalFree(sdPtr);
        }
    }

    /// <summary>
    /// Writes a FWPM_FILTER_CONDITION0 (40 bytes) at <paramref name="index"/> in the array.
    /// Use for pointer-type values (FWP_SECURITY_DESCRIPTOR_TYPE, FWP_BYTE_BLOB_TYPE, etc.).
    /// Layout: GUID fieldKey(16) | uint matchType(4) | [pad4] | uint valueType(4) | [pad4] | IntPtr valuePtr(8)
    /// </summary>
    private static void WriteCondition(IntPtr array, int index, Guid fieldKey,
        uint matchType, uint valueType, IntPtr valuePtr)
    {
        var p = IntPtr.Add(array, index * 40);
        Marshal.Copy(fieldKey.ToByteArray(), 0, p, 16); // fieldKey @ 0
        Marshal.WriteInt32(p, 16, (int)matchType); // matchType @ 16
        // [pad 4 @ 20]
        Marshal.WriteInt32(p, 24, (int)valueType); // conditionValue.type @ 24
        // [pad 4 @ 28]
        Marshal.WriteIntPtr(p, 32, valuePtr); // conditionValue.union_ptr @ 32
    }

    /// <summary>
    /// Writes a FWPM_FILTER_CONDITION0 (40 bytes) at <paramref name="index"/> in the array.
    /// Use for inline integer values (FWP_UINT8, FWP_UINT16, FWP_UINT32, flags bitmasks) stored
    /// directly in the union — value must fit in 32 bits; upper 4 bytes of the 8-byte slot are zeroed.
    /// </summary>
    private static void WriteConditionInline(IntPtr array, int index, Guid fieldKey,
        uint matchType, uint valueType, uint inlineValue)
    {
        var p = IntPtr.Add(array, index * 40);
        Marshal.Copy(fieldKey.ToByteArray(), 0, p, 16); // fieldKey @ 0
        Marshal.WriteInt32(p, 16, (int)matchType); // matchType @ 16
        // [pad 4 @ 20]
        Marshal.WriteInt32(p, 24, (int)valueType); // conditionValue.type @ 24
        // [pad 4 @ 28]
        Marshal.WriteInt32(p, 32, (int)inlineValue); // conditionValue.uintN inline @ 32
        // upper 4 bytes of 8-byte union slot already zeroed
    }

    /// <summary>
    /// Writes an FWPM_FILTER0 into the 200-byte buffer at <paramref name="filterPtr"/>.
    /// All pointer/output fields not set here are already zeroed.
    /// </summary>
    private static void WriteFilter(IntPtr filterPtr, Guid filterKey, bool isIPv6,
        IntPtr condArrayPtr, uint numConditions, IntPtr namePtr)
    {
        Marshal.Copy(filterKey.ToByteArray(), 0, filterPtr, 16); // filterKey @ 0
        Marshal.WriteIntPtr(filterPtr, 16, namePtr); // displayData.name @ 16
        // displayData.description @ 24 = null (zeroed)
        Marshal.WriteInt32(filterPtr, 32, (int)WfpNative.FWPM_FILTER_FLAG_PERSISTENT); // flags @ 32 = persistent filter (survives BFE restarts)
        // [pad 4 @ 36]
        // providerKey @ 40 = null (zeroed)
        // providerData @ 48..63 = zeroed (empty blob)

        var layer = isIPv6 ? WfpNative.LayerAleAuthConnectV6 : WfpNative.LayerAleAuthConnectV4;
        Marshal.Copy(layer.ToByteArray(), 0, IntPtr.Add(filterPtr, 64), 16); // layerKey @ 64

        // subLayerKey @ 80 = zero GUID = FWPM_SUBLAYER_UNIVERSAL (zeroed)

        // weight @ 96: type=FWP_EMPTY (0) = auto-assign (already zeroed).
        // IMPORTANT: FWP_UINT64 weight stores a *pointer* to UINT64 in the union, not an inline
        // value. Using FWP_EMPTY avoids dereferencing issues and lets WFP assign appropriate weight.

        Marshal.WriteInt32(filterPtr, 112, (int)numConditions); // numFilterConditions @ 112
        // [pad 4 @ 116]
        Marshal.WriteIntPtr(filterPtr, 120, condArrayPtr); // filterCondition @ 120

        Marshal.WriteInt32(filterPtr, 128, (int)WfpNative.FWP_ACTION_BLOCK); // action.type @ 128
        // action.providerContextKey @ 132 = zero GUID (zeroed)
        // [pad 4 @ 148]
        // rawContext @ 152 = 0 (zeroed)
        // reserved @ 168 = null (zeroed)
        // filterId @ 176 = 0 (output, zeroed)
        // effectiveWeight @ 184 = zeroed (output)
    }

    private static void ZeroMemory(IntPtr ptr, int length)
    {
        for (int i = 0; i < length; i++)
            Marshal.WriteByte(ptr, i, 0);
    }
}