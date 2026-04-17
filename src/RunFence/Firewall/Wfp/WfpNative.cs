using System.Runtime.InteropServices;

namespace RunFence.Firewall.Wfp;

/// <summary>
/// P/Invoke declarations for the Windows Filtering Platform (WFP) API.
/// Used for localhost blocking (INetFwRule cannot block loopback — Windows Firewall excludes
/// the loopback interface implicitly) and ICMP blocking (INetFwRule cannot reliably block ICMP
/// because Windows built-in ICMP allow rules override user-created block rules).
/// </summary>
public static class WfpNative
{
    public const uint ERROR_SUCCESS = 0;
    public const uint FWP_E_FILTER_NOT_FOUND = 0x80320003;
    public const uint FWPM_FILTER_FLAG_PERSISTENT = 0x00000001;

    // FWP_ACTION_TYPE: FWP_ACTION_FLAG_TERMINATING (0x1000) | 1 = block
    public const uint FWP_ACTION_BLOCK = 0x00001001;

    // FWP_MATCH_TYPE
    public const uint FWP_MATCH_EQUAL = 0;
    public const uint FWP_MATCH_RANGE = 5;
    public const uint FWP_MATCH_FLAGS_ANY_SET = 7;

    // FWP_DATA_TYPE (additional — FWP_RANGE_TYPE is above the basic 0..18 range)
    public const uint FWP_RANGE_TYPE = 0x102;

    // FWP_DATA_TYPE (FWP_EMPTY = 0 used implicitly via zeroed weight field)
    public const uint FWP_UINT8 = 1;                // inline byte (used for IP protocol condition)
    public const uint FWP_UINT16 = 2;
    public const uint FWP_UINT32 = 3;
    public const uint FWP_SECURITY_DESCRIPTOR_TYPE = 14;

    // IP protocol numbers (IPPROTO_*)
    public const uint IPPROTO_ICMP = 1;              // IPv4 ICMP
    public const uint IPPROTO_ICMPV6 = 58;           // IPv6 ICMPv6

    // FWP_CONDITION_FLAGS bitmask values (inline UINT32 in condition value union)
    public const uint FWP_CONDITION_FLAG_IS_LOOPBACK = 0x00000001;

    public const uint RPC_C_AUTHN_DEFAULT = 0xFFFFFFFF;
    public const uint SDDL_REVISION_1 = 1;

    // FWPM_LAYER_ALE_AUTH_CONNECT_V4: {c38d57d1-05a7-4c33-904f-7fbceee60e82}
    public static readonly Guid LayerAleAuthConnectV4 =
        new(0xc38d57d1, 0x05a7, 0x4c33, 0x90, 0x4f, 0x7f, 0xbc, 0xee, 0xe6, 0x0e, 0x82);

    // FWPM_LAYER_ALE_AUTH_CONNECT_V6: {4a72393b-319f-44bc-84c3-ba54dcb3b6b4}
    public static readonly Guid LayerAleAuthConnectV6 =
        new(0x4a72393b, 0x319f, 0x44bc, 0x84, 0xc3, 0xba, 0x54, 0xdc, 0xb3, 0xb6, 0xb4);

    // FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4: {e1cd9fe7-f4b5-4273-96c0-592e487b8650}
    public static readonly Guid LayerAleAuthRecvAcceptV4 =
        new(0xe1cd9fe7, 0xf4b5, 0x4273, 0x96, 0xc0, 0x59, 0x2e, 0x48, 0x7b, 0x86, 0x50);

    // FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6: {a3b42c97-9f04-4672-b87e-cee9c483257f}
    public static readonly Guid LayerAleAuthRecvAcceptV6 =
        new(0xa3b42c97, 0x9f04, 0x4672, 0xb8, 0x7e, 0xce, 0xe9, 0xc4, 0x83, 0x25, 0x7f);

    // FWPM_CONDITION_FLAGS: {632ce23b-5167-435c-86d7-e903684aa80c}
    public static readonly Guid ConditionFlags =
        new(0x632ce23b, 0x5167, 0x435c, 0x86, 0xd7, 0xe9, 0x03, 0x68, 0x4a, 0xa8, 0x0c);

    // FWPM_CONDITION_IP_REMOTE_PORT: {c35a604d-d22b-4e1a-91b4-68f674ee674b}
    public static readonly Guid ConditionIpRemotePort =
        new(0xc35a604d, 0xd22b, 0x4e1a, 0x91, 0xb4, 0x68, 0xf6, 0x74, 0xee, 0x67, 0x4b);

    // FWPM_CONDITION_ALE_USER_ID: {af043a0a-b34d-4f86-979c-c90371af6e66}
    public static readonly Guid ConditionAleUserId =
        new(0xaf043a0a, 0xb34d, 0x4f86, 0x97, 0x9c, 0xc9, 0x03, 0x71, 0xaf, 0x6e, 0x66);

    // FWPM_CONDITION_IP_PROTOCOL: {3971ef2b-623e-4f9a-8cb1-6e79b806b9a7}
    // Valid at FWPM_LAYER_ALE_AUTH_CONNECT_V4/V6
    public static readonly Guid ConditionIpProtocol =
        new(0x3971ef2b, 0x623e, 0x4f9a, 0x8c, 0xb1, 0x6e, 0x79, 0xb8, 0x06, 0xb9, 0xa7);

    // FWPM_CONDITION_IP_REMOTE_ADDRESS: {b235ae9a-1d64-49b8-a44c-5ff3d9095045}
    public static readonly Guid ConditionIpRemoteAddress =
        new(0xb235ae9a, 0x1d64, 0x49b8, 0xa4, 0x4c, 0x5f, 0xf3, 0xd9, 0x09, 0x50, 0x45);

    // FWP_DATA_TYPE for address-and-mask subnet conditions (above FWP_SINGLE_DATA_TYPE_MAX = 0xFF)
    public const uint FWP_V4_ADDR_MASK = 0x100;   // FWP_V4_ADDR_AND_MASK struct (8 bytes: addr uint32 + mask uint32)
    public const uint FWP_V6_ADDR_MASK = 0x101;   // FWP_V6_ADDR_AND_MASK struct (17 bytes: addr[16] + prefixLength uint8)

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    public static extern uint FwpmEngineOpen0(
        string? serverName,
        uint authnService,
        IntPtr authIdentity,
        IntPtr session,
        out IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmFilterAdd0(
        IntPtr engineHandle,
        IntPtr filter, // FWPM_FILTER0* — WFP makes a deep copy
        IntPtr sd, // optional security descriptor for the filter object itself
        out ulong filterId);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmFilterDeleteByKey0(
        IntPtr engineHandle,
        ref Guid key);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmEngineClose0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmTransactionBegin0(IntPtr engineHandle, uint flags);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmTransactionCommit0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmTransactionAbort0(IntPtr engineHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSDRevision,
        out IntPtr securityDescriptor,
        out uint securityDescriptorSize);
}