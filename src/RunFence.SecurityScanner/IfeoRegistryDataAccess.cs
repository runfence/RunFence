using System.Security.AccessControl;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

/// <summary>
/// Implements registry data access for Image File Execution Options (IFEO).
/// </summary>
public class IfeoRegistryDataAccess : IIfeoRegistryAccess
{
    private const string NativeRootPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string Wow6432RootPath = @"SOFTWARE\Wow6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    public RegistrySecurity? GetIfeoRegistryKeySecurity()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(NativeRootPath,
                RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
            return key?.GetAccessControl();
        }
        catch
        {
            return null;
        }
    }

    public RegistrySecurity? GetIfeoWow6432RegistryKeySecurity()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Wow6432RootPath,
                RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
            return key?.GetAccessControl();
        }
        catch
        {
            return null;
        }
    }

    public List<IfeoSubkeyInfo> GetIfeoSubkeys()
    {
        var subkeys = new List<IfeoSubkeyInfo>();
        CollectSubkeys(NativeRootPath, @"HKLM\...\Image File Execution Options",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", subkeys);
        CollectSubkeys(Wow6432RootPath, @"HKLM\...\Wow6432Node\Image File Execution Options",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", subkeys);
        return subkeys;
    }

    private static void CollectSubkeys(
        string rootPath,
        string displayRoot,
        string navigationRoot,
        List<IfeoSubkeyInfo> subkeys)
    {
        try
        {
            using var rootKey = Registry.LocalMachine.OpenSubKey(rootPath);
            if (rootKey == null)
                return;

            foreach (var exeName in rootKey.GetSubKeyNames())
            {
                try
                {
                    using var subkey = rootKey.OpenSubKey(exeName);
                    if (subkey == null)
                        continue;

                    RegistrySecurity? security = null;
                    try
                    {
                        using var secKey = Registry.LocalMachine.OpenSubKey(
                            $@"{rootPath}\{exeName}",
                            RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
                        security = secKey?.GetAccessControl();
                    }
                    catch
                    {
                        /* best effort */
                    }

                    var debuggerValue = subkey.GetValue("Debugger") as string;
                    var verifierDlls = subkey.GetValue("VerifierDlls") as string;
                    var debuggerPath = string.IsNullOrWhiteSpace(debuggerValue)
                        ? null
                        : CommandLineParser.ExtractExecutablePath(debuggerValue);

                    subkeys.Add(new IfeoSubkeyInfo(
                        exeName,
                        $@"{displayRoot}\{exeName}",
                        $@"{navigationRoot}\{exeName}",
                        security,
                        debuggerPath,
                        verifierDlls));
                }
                catch
                {
                    /* skip individual subkey */
                }
            }
        }
        catch
        {
            /* not accessible */
        }
    }
}
