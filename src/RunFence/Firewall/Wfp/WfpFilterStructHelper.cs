using System.Runtime.InteropServices;

namespace RunFence.Firewall.Wfp;

/// <summary>
/// Pure memory layout helpers for WFP filter structs. Contains only side-effect-free marshal
/// operations — no I/O, no logging, no P/Invoke calls. Used by <see cref="WfpFilterHelperService"/>
/// and <see cref="WfpGlobalIcmpBlocker"/> to avoid duplication of binary layout logic.
/// </summary>
internal static class WfpFilterStructHelper
{
    /// <summary>
    /// Writes a FWPM_FILTER_CONDITION0 (40 bytes) at <paramref name="index"/> in the array.
    /// Use for pointer-type values (FWP_SECURITY_DESCRIPTOR_TYPE, FWP_BYTE_BLOB_TYPE, etc.).
    /// Layout: GUID fieldKey(16) | uint matchType(4) | [pad4] | uint valueType(4) | [pad4] | IntPtr valuePtr(8)
    /// </summary>
    public static void WriteCondition(IntPtr array, int index, Guid fieldKey,
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
    public static void WriteConditionInline(IntPtr array, int index, Guid fieldKey,
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
    /// Weight defaults to FWP_EMPTY (auto-assigned by WFP).
    /// All pointer/output fields not set here are already zeroed.
    /// </summary>
    public static void WriteFilter(IntPtr filterPtr, Guid filterKey, bool isIPv6,
        IntPtr condArrayPtr, uint numConditions, IntPtr namePtr, uint action,
        uint flags = WfpNative.FWPM_FILTER_FLAG_PERSISTENT)
    {
        var layerKey = isIPv6 ? WfpNative.LayerAleAuthConnectV6 : WfpNative.LayerAleAuthConnectV4;
        WriteFilter(filterPtr, filterKey, layerKey, condArrayPtr, numConditions, namePtr, action, flags);
    }

    /// <summary>
    /// Writes an FWPM_FILTER0 into the 200-byte buffer at <paramref name="filterPtr"/> using an
    /// explicit <paramref name="layerKey"/>. Weight defaults to FWP_EMPTY (auto-assigned by WFP).
    /// All pointer/output fields not set here are already zeroed.
    /// </summary>
    public static void WriteFilter(IntPtr filterPtr, Guid filterKey, Guid layerKey,
        IntPtr condArrayPtr, uint numConditions, IntPtr namePtr, uint action,
        uint flags = WfpNative.FWPM_FILTER_FLAG_PERSISTENT)
    {
        Marshal.Copy(filterKey.ToByteArray(), 0, filterPtr, 16); // filterKey @ 0
        Marshal.WriteIntPtr(filterPtr, 16, namePtr); // displayData.name @ 16
        // displayData.description @ 24 = null (zeroed)
        Marshal.WriteInt32(filterPtr, 32, (int)flags); // flags @ 32
        // [pad 4 @ 36]
        // providerKey @ 40 = null (zeroed)
        // providerData @ 48..63 = zeroed (empty blob)

        Marshal.Copy(layerKey.ToByteArray(), 0, IntPtr.Add(filterPtr, 64), 16); // layerKey @ 64

        // subLayerKey @ 80 = zero GUID = FWPM_SUBLAYER_UNIVERSAL (zeroed)

        // weight @ 96: type=FWP_EMPTY (0) = auto-assign (already zeroed).

        Marshal.WriteInt32(filterPtr, 112, (int)numConditions); // numFilterConditions @ 112
        // [pad 4 @ 116]
        Marshal.WriteIntPtr(filterPtr, 120, condArrayPtr); // filterCondition @ 120

        Marshal.WriteInt32(filterPtr, 128, (int)action); // action.type @ 128
        // action.providerContextKey @ 132 = zero GUID (zeroed)
        // [pad 4 @ 148]
        // rawContext @ 152 = 0 (zeroed)
        // reserved @ 168 = null (zeroed)
        // filterId @ 176 = 0 (output, zeroed)
        // effectiveWeight @ 184 = zeroed (output)
    }

    /// <summary>
    /// Writes a FWPM_FILTER_CONDITION0 (40 bytes) at <paramref name="index"/> in the array
    /// with FWP_MATCH_RANGE and FWP_RANGE_TYPE, pointing to a FWP_RANGE0 struct.
    /// The FWP_RANGE0 is allocated and added to <paramref name="marshalAllocs"/>.
    ///
    /// FWP_RANGE0 layout (32 bytes):
    ///   FWP_VALUE0 valueLow  (16): type(4) + [pad4] + value(8)
    ///   FWP_VALUE0 valueHigh (16): type(4) + [pad4] + value(8)
    /// </summary>
    public static void WriteConditionRange(IntPtr array, int index, Guid fieldKey,
        uint valueType, uint low, uint high, List<IntPtr> marshalAllocs)
    {
        var rangePtr = Marshal.AllocHGlobal(32);
        marshalAllocs.Add(rangePtr);
        ZeroMemory(rangePtr, 32);
        // valueLow
        Marshal.WriteInt32(rangePtr, 0, (int)valueType);
        Marshal.WriteInt32(rangePtr, 8, (int)low);
        // valueHigh
        Marshal.WriteInt32(rangePtr, 16, (int)valueType);
        Marshal.WriteInt32(rangePtr, 24, (int)high);

        WriteCondition(array, index, fieldKey,
            WfpNative.FWP_MATCH_RANGE, WfpNative.FWP_RANGE_TYPE, rangePtr);
    }

    /// <summary>
    /// Writes an FWPM_FILTER_CONDITION0 (40 bytes) at <paramref name="index"/> in the array
    /// with FWP_MATCH_EQUAL and FWP_V4_ADDR_MASK, for IPv4 subnet matching.
    /// Allocates an 8-byte FWP_V4_ADDR_AND_MASK struct { uint32 addr; uint32 mask } in
    /// host byte order and adds it to <paramref name="marshalAllocs"/>.
    /// </summary>
    public static void WriteConditionV4Subnet(IntPtr array, int index,
        uint addr, uint mask, List<IntPtr> marshalAllocs)
    {
        var structPtr = Marshal.AllocHGlobal(8);
        marshalAllocs.Add(structPtr);
        ZeroMemory(structPtr, 8);
        Marshal.WriteInt32(structPtr, 0, (int)addr);
        Marshal.WriteInt32(structPtr, 4, (int)mask);
        WriteCondition(array, index, WfpNative.ConditionIpRemoteAddress,
            WfpNative.FWP_MATCH_EQUAL, WfpNative.FWP_V4_ADDR_MASK, structPtr);
    }

    /// <summary>
    /// Writes an FWPM_FILTER_CONDITION0 (40 bytes) at <paramref name="index"/> in the array
    /// with FWP_MATCH_EQUAL and FWP_V6_ADDR_AND_MASK, for IPv6 subnet matching.
    /// Allocates a 17-byte FWP_V6_ADDR_AND_MASK struct { byte[16] addr; byte prefixLength }
    /// and adds it to <paramref name="marshalAllocs"/>.
    /// </summary>
    public static void WriteConditionV6Subnet(IntPtr array, int index,
        byte[] addrBytes, byte prefixLength, List<IntPtr> marshalAllocs)
    {
        var structPtr = Marshal.AllocHGlobal(17);
        marshalAllocs.Add(structPtr);
        ZeroMemory(structPtr, 17);
        Marshal.Copy(addrBytes, 0, structPtr, 16);
        Marshal.WriteByte(structPtr, 16, prefixLength);
        WriteCondition(array, index, WfpNative.ConditionIpRemoteAddress,
            WfpNative.FWP_MATCH_EQUAL, WfpNative.FWP_V6_ADDR_MASK, structPtr);
    }


    /// <summary>
    /// Creates a 16-byte FWP_BYTE_BLOB struct pointing to an SD; adds blob pointer to
    /// <paramref name="marshalAllocs"/> for cleanup via Marshal.FreeHGlobal.
    /// </summary>
    public static IntPtr CreateSdBlob(IntPtr sdPtr, uint sdSize, List<IntPtr> marshalAllocs)
    {
        var sdBlobPtr = Marshal.AllocHGlobal(16);
        marshalAllocs.Add(sdBlobPtr);
        ZeroMemory(sdBlobPtr, 16);
        Marshal.WriteInt32(sdBlobPtr, 0, (int)sdSize);
        Marshal.WriteIntPtr(sdBlobPtr, 8, sdPtr);
        return sdBlobPtr;
    }

    public static void ZeroMemory(IntPtr ptr, int length)
    {
        for (int i = 0; i < length; i++)
            Marshal.WriteByte(ptr, i, 0);
    }
}
