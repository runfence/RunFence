using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Account;

public class LocalAccountProvisioningService : ILocalAccountProvisioningService
{
    public void CreateLocalUser(string username, ProtectedString password)
    {
        IntPtr namePtr = IntPtr.Zero;
        IntPtr passwordPtr = IntPtr.Zero;
        IntPtr structPtr = IntPtr.Zero;
        try
        {
            namePtr = Marshal.StringToHGlobalUni(username);
            passwordPtr = password.AllocUnicode();

            var info = new WindowsAccountNative.USER_INFO_1_LAYOUT
            {
                usri1_name = namePtr,
                usri1_password = passwordPtr,
                usri1_password_age = 0,
                usri1_priv = (int)WindowsAccountNative.USER_PRIV_USER,
                usri1_home_dir = IntPtr.Zero,
                usri1_comment = IntPtr.Zero,
                usri1_flags = (int)(WindowsAccountNative.UF_NORMAL_ACCOUNT | WindowsAccountNative.UF_DONT_EXPIRE_PASSWD),
                usri1_script_path = IntPtr.Zero
            };

            structPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsAccountNative.USER_INFO_1_LAYOUT>());
            Marshal.StructureToPtr(info, structPtr, false);

            int result = WindowsAccountNative.NetUserAdd(null, 1, structPtr, out uint parmErr);
            if (result == 0)
                return;

            var msg = result switch
            {
                2224 => $"Account name '{username}' is already in use.",
                2245 => "The password does not meet the requirements.",
                _ => $"NetUserAdd failed with error code {result} (parameter index {parmErr})."
            };
            throw new InvalidOperationException(msg);
        }
        finally
        {
            if (passwordPtr != IntPtr.Zero)
                Marshal.ZeroFreeGlobalAllocUnicode(passwordPtr);
            if (namePtr != IntPtr.Zero)
                Marshal.FreeHGlobal(namePtr);
            if (structPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(structPtr);
        }
    }

    public int SetDisplayName(string username, string displayName)
    {
        IntPtr fullNamePtr = IntPtr.Zero;
        IntPtr displayNamePtr = IntPtr.Zero;
        try
        {
            fullNamePtr = Marshal.AllocHGlobal(IntPtr.Size);
            displayNamePtr = Marshal.StringToHGlobalUni(displayName);
            Marshal.WriteIntPtr(fullNamePtr, displayNamePtr);
            return WindowsAccountNative.NetUserSetInfo(null, username, 1011, fullNamePtr, out _);
        }
        finally
        {
            if (displayNamePtr != IntPtr.Zero)
                Marshal.FreeHGlobal(displayNamePtr);
            if (fullNamePtr != IntPtr.Zero)
                Marshal.FreeHGlobal(fullNamePtr);
        }
    }

    public int RenameLocalUser(string currentUsername, string newUsername)
    {
        var info = new WindowsAccountNative.USER_INFO_0 { usri0_name = newUsername };
        return WindowsAccountNative.NetUserSetInfo(null, currentUsername, 0, ref info, out _);
    }

    public void DeleteLocalUserBySid(string sid)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
        user?.Delete();
    }

    public void DeleteLocalUserByName(string username)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, username);
        user?.Delete();
    }
}
