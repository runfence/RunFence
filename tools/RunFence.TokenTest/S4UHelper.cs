using System.Runtime.InteropServices;
using System.Text;
using RunFence.TokenTest.Native;

namespace RunFence.TokenTest;

internal class S4UHelper
{
    private readonly SystemTokenHelper _systemTokenHelper;

    public S4UHelper(SystemTokenHelper systemTokenHelper)
    {
        _systemTokenHelper = systemTokenHelper;
    }

    public IntPtr GetS4UToken(string username, TokenNative.SECURITY_LOGON_TYPE logonType = TokenNative.SECURITY_LOGON_TYPE.Network)
    {
        _systemTokenHelper.EnableDebugPrivilege();

        IntPtr hSystemToken = _systemTokenHelper.GetSystemToken();
        if (hSystemToken == IntPtr.Zero)
            throw new InvalidOperationException($"Could not get SYSTEM token: {TokenHelper.GetLastError()}");

        // LsaLogonUser S4U requires a TRUSTED LSA connection (LsaRegisterLogonProcess, not LsaConnectUntrusted).
        // LsaRegisterLogonProcess requires SeTcbPrivilege which SYSTEM holds; impersonate SYSTEM for this call.
        if (!_systemTokenHelper.ImpersonateToken(hSystemToken))
        {
            ProcessNative.CloseHandle(hSystemToken);
            throw new InvalidOperationException($"ImpersonateToken(SYSTEM) failed: {TokenHelper.GetLastError()}");
        }
        ProcessNative.CloseHandle(hSystemToken);

        IntPtr hLsa = IntPtr.Zero;
        IntPtr authBuffer = IntPtr.Zero;
        IntPtr logonNamePtr = IntPtr.Zero;
        try
        {
            byte[] logonProcessNameBytes = Encoding.ASCII.GetBytes("TokenTest");
            logonNamePtr = Marshal.AllocHGlobal(logonProcessNameBytes.Length);
            Marshal.Copy(logonProcessNameBytes, 0, logonNamePtr, logonProcessNameBytes.Length);
            var logonProcessName = new LsaNative.LSA_STRING
            {
                Length = (ushort)logonProcessNameBytes.Length,
                MaximumLength = (ushort)(logonProcessNameBytes.Length + 1),
                Buffer = logonNamePtr
            };

            uint status = LsaNative.LsaRegisterLogonProcess(ref logonProcessName, out hLsa, out _);
            Marshal.FreeHGlobal(logonNamePtr);
            logonNamePtr = IntPtr.Zero;
            if (status != 0)
                throw new InvalidOperationException($"LsaRegisterLogonProcess failed: NTSTATUS=0x{status:X8}");

            byte[] pkgNameBytes = Encoding.ASCII.GetBytes("MICROSOFT_AUTHENTICATION_PACKAGE_V1_0");
            IntPtr pkgNamePtr = Marshal.AllocHGlobal(pkgNameBytes.Length);
            Marshal.Copy(pkgNameBytes, 0, pkgNamePtr, pkgNameBytes.Length);
            var pkgName = new LsaNative.LSA_STRING
            {
                Length = (ushort)pkgNameBytes.Length,
                MaximumLength = (ushort)pkgNameBytes.Length,
                Buffer = pkgNamePtr
            };
            status = LsaNative.LsaLookupAuthenticationPackage(hLsa, ref pkgName, out uint authPkg);
            Marshal.FreeHGlobal(pkgNamePtr);
            if (status != 0)
                throw new InvalidOperationException($"LsaLookupAuthenticationPackage failed: NTSTATUS=0x{status:X8}");

            byte[] usernameBytes = Encoding.Unicode.GetBytes(username);
            byte[] domainBytes = Encoding.Unicode.GetBytes(".");

            int structSize = Marshal.SizeOf<LsaNative.MSV1_0_S4U_LOGON>();
            int totalSize = structSize + usernameBytes.Length + domainBytes.Length;
            authBuffer = Marshal.AllocHGlobal(totalSize);
            for (int i = 0; i < totalSize; i++) Marshal.WriteByte(authBuffer, i, 0);

            IntPtr usernamePtr = authBuffer + structSize;
            IntPtr domainPtr = usernamePtr + usernameBytes.Length;
            Marshal.Copy(usernameBytes, 0, usernamePtr, usernameBytes.Length);
            Marshal.Copy(domainBytes, 0, domainPtr, domainBytes.Length);

            var s4u = new LsaNative.MSV1_0_S4U_LOGON
            {
                MessageType = 12, // MsV1_0S4ULogon
                Flags = 0,
                UserPrincipalName = new LsaNative.LSA_UNICODE_STRING
                {
                    Length = (ushort)usernameBytes.Length,
                    MaximumLength = (ushort)usernameBytes.Length,
                    Buffer = (IntPtr)structSize   // relative offset from authBuffer start
                },
                DomainName = new LsaNative.LSA_UNICODE_STRING
                {
                    Length = (ushort)domainBytes.Length,
                    MaximumLength = (ushort)domainBytes.Length,
                    Buffer = (IntPtr)(structSize + usernameBytes.Length)  // relative offset
                }
            };
            Marshal.StructureToPtr(s4u, authBuffer, false);

            if (!TokenNative.AllocateLocallyUniqueId(out var srcLuid))
                throw new InvalidOperationException($"AllocateLocallyUniqueId failed: {TokenHelper.GetLastError()}");

            var tokenSource = new LsaNative.TOKEN_SOURCE
            {
                SourceName = Encoding.ASCII.GetBytes("TokenTst"),
                SourceIdentifier = srcLuid
            };

            byte[] originNameBytes = Encoding.ASCII.GetBytes("TokenTest");
            IntPtr originNamePtr = Marshal.AllocHGlobal(originNameBytes.Length);
            Marshal.Copy(originNameBytes, 0, originNamePtr, originNameBytes.Length);
            var originName = new LsaNative.LSA_STRING
            {
                Length = (ushort)originNameBytes.Length,
                MaximumLength = (ushort)(originNameBytes.Length + 1),
                Buffer = originNamePtr
            };

            status = LsaNative.LsaLogonUser(
                hLsa,
                ref originName,
                (uint)logonType,
                authPkg,
                authBuffer,
                (uint)totalSize,
                IntPtr.Zero,
                ref tokenSource,
                out IntPtr profileBuffer,
                out uint profileLength,
                out _,
                out IntPtr hToken,
                out _,
                out uint subStatus);

            Marshal.FreeHGlobal(originNamePtr);

            if (status != 0)
                throw new InvalidOperationException($"LsaLogonUser failed: NTSTATUS=0x{status:X8} subStatus=0x{subStatus:X8}");

            if (profileBuffer != IntPtr.Zero)
                LsaNative.LsaFreeReturnBuffer(profileBuffer);

            return hToken;
        }
        finally
        {
            _systemTokenHelper.RevertImpersonation();
            if (authBuffer != IntPtr.Zero) Marshal.FreeHGlobal(authBuffer);
            if (logonNamePtr != IntPtr.Zero) Marshal.FreeHGlobal(logonNamePtr);
            if (hLsa != IntPtr.Zero) LsaNative.LsaDeregisterLogonProcess(hLsa);
        }
    }
}
