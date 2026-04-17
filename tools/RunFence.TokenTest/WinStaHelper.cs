using System.Runtime.InteropServices;
using RunFence.TokenTest.Native;

namespace RunFence.TokenTest;

internal class WinStaHelper
{
    public void PrintDacl(string objectName, IntPtr hObject, SecurityNative.SE_OBJECT_TYPE objectType)
    {
        Console.WriteLine($"\n--- DACL for {objectName} ---");
        uint err = SecurityNative.GetSecurityInfo(
            hObject, objectType,
            SecurityNative.DACL_SECURITY_INFORMATION,
            IntPtr.Zero, IntPtr.Zero,
            out IntPtr pDacl, IntPtr.Zero,
            out IntPtr pSd);

        if (err != 0)
        {
            Console.WriteLine($"  GetSecurityInfo failed: {TokenHelper.FormatError(err)}");
            return;
        }

        try
        {
            if (pDacl == IntPtr.Zero)
            {
                Console.WriteLine("  (NULL DACL — unrestricted access)");
                return;
            }

            var aclInfo = new SecurityNative.ACL_SIZE_INFORMATION();
            if (!SecurityNative.GetAclInformation(pDacl, ref aclInfo,
                    (uint)Marshal.SizeOf<SecurityNative.ACL_SIZE_INFORMATION>(),
                    SecurityNative.ACL_INFORMATION_CLASS.AclSizeInformation))
            {
                Console.WriteLine($"  GetAclInformation failed: {TokenHelper.GetLastError()}");
                return;
            }

            Console.WriteLine($"  ACE count: {aclInfo.AceCount}");
            for (uint i = 0; i < aclInfo.AceCount; i++)
            {
                if (!SecurityNative.GetAce(pDacl, i, out IntPtr pAce))
                {
                    Console.WriteLine($"  GetAce({i}) failed: {TokenHelper.GetLastError()}");
                    continue;
                }

                var header = Marshal.PtrToStructure<SecurityNative.ACE_HEADER>(pAce);
                string aceType = header.AceType switch
                {
                    SecurityNative.ACCESS_ALLOWED_ACE_TYPE => "ALLOW",
                    SecurityNative.ACCESS_DENIED_ACE_TYPE  => "DENY",
                    _ => $"TYPE={header.AceType}"
                };

                // SID starts at offset of ACE_HEADER + Mask (4 bytes) = 8 bytes from ace start
                IntPtr pSid = pAce + Marshal.SizeOf<SecurityNative.ACE_HEADER>() + 4;
                uint mask = (uint)Marshal.ReadInt32(pAce, Marshal.SizeOf<SecurityNative.ACE_HEADER>());

                SecurityNative.ConvertSidToStringSid(pSid, out var sidStr);
                Console.WriteLine($"  [{i}] {aceType} {sidStr ?? "?"} Mask=0x{mask:X8} Flags=0x{header.AceFlags:X2}");
            }
        }
        finally
        {
            SecurityNative.LocalFree(pSd);
        }
    }

    public bool AddSidToWindowStation(IntPtr sid)
    {
        IntPtr hWinSta = WinStaNative.GetProcessWindowStation();
        if (hWinSta == IntPtr.Zero) return false;
        return AddSidToObject(sid, hWinSta, SecurityNative.SE_OBJECT_TYPE.SE_WINDOW_OBJECT,
            SecurityNative.WINSTA_ALL_ACCESS | SecurityNative.GENERIC_ALL | SecurityNative.STANDARD_RIGHTS_ALL);
    }

    public bool AddSidToDesktop(IntPtr sid)
    {
        IntPtr hDesktop = WinStaNative.OpenDesktop("Default", 0, false,
            SecurityNative.GENERIC_ALL | SecurityNative.READ_CONTROL | SecurityNative.WRITE_DAC);
        if (hDesktop == IntPtr.Zero) return false;
        try
        {
            return AddSidToObject(sid, hDesktop, SecurityNative.SE_OBJECT_TYPE.SE_WINDOW_OBJECT,
                SecurityNative.DESKTOP_ALL_ACCESS | SecurityNative.GENERIC_ALL | SecurityNative.STANDARD_RIGHTS_ALL);
        }
        finally
        {
            WinStaNative.CloseDesktop(hDesktop);
        }
    }

    public bool ReadAndLowerMandatoryLabel(IntPtr hObject, SecurityNative.SE_OBJECT_TYPE objType, string objectName)
    {
        Console.WriteLine($"  [label] {objectName}: reading mandatory label...");

        uint err = SecurityNative.GetSecurityInfoWithSacl(
            hObject, objType,
            SecurityNative.LABEL_SECURITY_INFORMATION,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            out IntPtr pSacl,
            out IntPtr pSd);

        if (err != 0)
        {
            Marshal.SetLastSystemError((int)err);
            Console.WriteLine($"  [label] GetSecurityInfo(SACL) failed: {TokenHelper.FormatError(err)}");
            return false;
        }

        try
        {
            if (pSacl == IntPtr.Zero)
            {
                Console.WriteLine($"  [label] {objectName}: no SACL — no mandatory label");
                return true;
            }

            var aclInfo = new SecurityNative.ACL_SIZE_INFORMATION();
            if (!SecurityNative.GetAclInformation(pSacl, ref aclInfo,
                    (uint)Marshal.SizeOf<SecurityNative.ACL_SIZE_INFORMATION>(),
                    SecurityNative.ACL_INFORMATION_CLASS.AclSizeInformation))
            {
                Console.WriteLine($"  [label] GetAclInformation failed: {TokenHelper.GetLastError()}");
                return false;
            }

            uint currentIL = 0;
            for (uint i = 0; i < aclInfo.AceCount; i++)
            {
                if (!SecurityNative.GetAce(pSacl, i, out IntPtr pAce)) continue;
                byte aceType = Marshal.ReadByte(pAce, 0);
                if (aceType != SecurityNative.SYSTEM_MANDATORY_LABEL_ACE_TYPE) continue;

                // SID starts at offset 8: ACE_HEADER(4) + AccessMask(4)
                IntPtr pLabelSid = pAce + 8;
                SecurityNative.ConvertSidToStringSid(pLabelSid, out var labelStr);
                Console.WriteLine($"  [label] {objectName}: current label = {labelStr ?? "?"}");

                // S-1-16-RID: sub-authority value (4 bytes) at offset 8 from SID base
                currentIL = (uint)Marshal.ReadInt32(pLabelSid, 8);
                break;
            }

            if (currentIL == 0)
            {
                Console.WriteLine($"  [label] {objectName}: no mandatory label ACE found in SACL");
                return true;
            }

            if (currentIL < 12288)
            {
                Console.WriteLine($"  [label] {objectName}: label IL={currentIL} is Medium or lower — no change needed");
                return true;
            }

            // Lower to Medium
            uint cbMediumSid = 256;
            IntPtr pMediumSid = Marshal.AllocHGlobal((int)cbMediumSid);
            try
            {
                if (!SecurityNative.CreateWellKnownSid(SecurityNative.WELL_KNOWN_SID_TYPE.WinMediumLabelSid,
                        IntPtr.Zero, pMediumSid, ref cbMediumSid))
                {
                    Console.WriteLine($"  [label] CreateWellKnownSid(Medium) failed: {TokenHelper.GetLastError()}");
                    return false;
                }

                IntPtr pNewSacl = Marshal.AllocHGlobal(256);
                try
                {
                    if (!SecurityNative.InitializeAcl(pNewSacl, 256, 2))
                    {
                        Console.WriteLine($"  [label] InitializeAcl failed: {TokenHelper.GetLastError()}");
                        return false;
                    }

                    if (!SecurityNative.AddMandatoryAce(pNewSacl, 2, 0,
                            SecurityNative.SYSTEM_MANDATORY_LABEL_NO_WRITE_UP, pMediumSid))
                    {
                        Console.WriteLine($"  [label] AddMandatoryAce failed: {TokenHelper.GetLastError()}");
                        return false;
                    }

                    err = SecurityNative.SetSecurityInfo(
                        hObject, objType,
                        SecurityNative.LABEL_SECURITY_INFORMATION,
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, pNewSacl);

                    if (err != 0)
                    {
                        Marshal.SetLastSystemError((int)err);
                        Console.WriteLine($"  [label] SetSecurityInfo(SACL) failed: {TokenHelper.FormatError(err)}");
                        return false;
                    }

                    Console.WriteLine($"  [label] {objectName}: lowered mandatory label to Medium (was IL={currentIL})");
                    return true;
                }
                finally { Marshal.FreeHGlobal(pNewSacl); }
            }
            finally { Marshal.FreeHGlobal(pMediumSid); }
        }
        finally { SecurityNative.LocalFree(pSd); }
    }

    private static bool AddSidToObject(IntPtr sid, IntPtr hObject, SecurityNative.SE_OBJECT_TYPE objType, uint accessMask)
    {
        uint err = SecurityNative.GetSecurityInfo(
            hObject, objType,
            SecurityNative.DACL_SECURITY_INFORMATION,
            IntPtr.Zero, IntPtr.Zero,
            out IntPtr pOldDacl, IntPtr.Zero,
            out IntPtr pSd);

        if (err != 0)
        {
            Marshal.SetLastSystemError((int)err);
            return false;
        }

        try
        {
            var ea = new SecurityNative.EXPLICIT_ACCESS[]
            {
                new()
                {
                    grfAccessPermissions = accessMask,
                    grfAccessMode = SecurityNative.ACCESS_MODE.SET_ACCESS,
                    grfInheritance = 0x3, // CONTAINER_INHERIT_ACE | OBJECT_INHERIT_ACE
                    Trustee = new SecurityNative.TRUSTEE
                    {
                        pMultipleTrustee = IntPtr.Zero,
                        MultipleTrusteeOperation = SecurityNative.MULTIPLE_TRUSTEE_OPERATION.NO_MULTIPLE_TRUSTEE,
                        TrusteeForm = SecurityNative.TRUSTEE_FORM.TRUSTEE_IS_SID,
                        TrusteeType = SecurityNative.TRUSTEE_TYPE.TRUSTEE_IS_UNKNOWN,
                        ptstrName = sid
                    }
                }
            };

            err = SecurityNative.SetEntriesInAcl(1, ea, pOldDacl, out IntPtr pNewDacl);
            if (err != 0)
            {
                Marshal.SetLastSystemError((int)err);
                return false;
            }

            try
            {
                err = SecurityNative.SetSecurityInfo(
                    hObject, objType,
                    SecurityNative.DACL_SECURITY_INFORMATION,
                    IntPtr.Zero, IntPtr.Zero, pNewDacl, IntPtr.Zero);

                if (err != 0)
                {
                    Marshal.SetLastSystemError((int)err);
                    return false;
                }
                return true;
            }
            finally
            {
                SecurityNative.LocalFree(pNewDacl);
            }
        }
        finally
        {
            SecurityNative.LocalFree(pSd);
        }
    }
}
