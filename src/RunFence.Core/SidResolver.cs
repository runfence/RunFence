using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using RunFence.Core.Models;

namespace RunFence.Core;

/// <summary>
/// Default implementation of <see cref="ISidResolver"/> that delegates to the Windows
/// identity subsystem. Use <see cref="SidResolutionHelper.GetCurrentUserSid"/> for
/// the current-user SID in code that cannot take constructor DI (e.g. model properties).
/// </summary>
public class SidResolver : ISidResolver
{
    private readonly NTTranslateApi _ntTranslate;
    private readonly ILoggingService _log;
    private readonly string? _localMachineSidPrefix;

    public SidResolver(NTTranslateApi ntTranslate, ILoggingService log)
    {
        _ntTranslate = ntTranslate;
        _log = log;
        _localMachineSidPrefix = TryGetLocalMachineSidPrefix();
    }

    public string? TryResolveSid(string accountName)
    {
        // Try local SAM first — direct query with explicit machine name, no DC lookup.
        var localSid = TryResolveSidLocal(accountName);
        if (localSid != null)
            return localSid;

        // Fall back to OS resolution for domain accounts (may contact DC).
        try
        {
            var sid = _ntTranslate.TranslateSid(accountName);
            return sid.Value;
        }
        catch
        {
            return null;
        }
    }

    private string? TryResolveSidLocal(string accountName)
    {
        uint sidSize = 0;
        uint domainSize = 0;
        SidResolverNative.LookupAccountNameW(Environment.MachineName, accountName, IntPtr.Zero, ref sidSize, null, ref domainSize, out _);
        if (sidSize == 0)
            return null;

        var sidBuffer = Marshal.AllocHGlobal((int)sidSize);
        try
        {
            var domain = new StringBuilder((int)domainSize);
            if (!SidResolverNative.LookupAccountNameW(Environment.MachineName, accountName, sidBuffer, ref sidSize, domain, ref domainSize, out _))
                return null;

            if (!SidResolverNative.ConvertSidToStringSidW(sidBuffer, out var stringSidPtr))
                return null;
            try
            {
                return Marshal.PtrToStringUni(stringSidPtr);
            }
            finally
            {
                SidResolverNative.LocalFree(stringSidPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(sidBuffer);
        }
    }

    public string? TryResolveName(string sidString)
    {
        // Try local SAM first — direct query with explicit machine name, no DC lookup.
        // SecurityIdentifier.Translate(NTAccount) with null system name may contact a DC,
        // causing 30-60s timeouts on VMs or machines with unreachable domain controllers.
        var localName = TryResolveNameLocal(sidString);
        if (localName != null)
            return localName;

        // Only fall back to OS resolution for domain SIDs (may contact DC).
        // For local/well-known SIDs, local lookup is authoritative — skip the slow fallback.
        if (!IsDomainSid(sidString))
            return null;

        try
        {
            var ntAccount = _ntTranslate.TranslateName(sidString);
            return ntAccount.Value;
        }
        catch
        {
            return null;
        }
    }

    private string? TryResolveNameLocal(string sidString)
    {
        if (!SidResolverNative.ConvertStringSidToSidW(sidString, out var sidPtr))
            return null;
        try
        {
            uint nameSize = 0;
            uint domainSize = 0;
            SidResolverNative.LookupAccountSidW(Environment.MachineName, sidPtr, null, ref nameSize, null, ref domainSize, out _);
            if (nameSize == 0)
                return null;

            var name = new StringBuilder((int)nameSize);
            var domain = new StringBuilder((int)domainSize);
            if (!SidResolverNative.LookupAccountSidW(Environment.MachineName, sidPtr, name, ref nameSize, domain, ref domainSize, out _))
                return null;

            var domainStr = domain.ToString();
            var nameStr = name.ToString();
            return string.IsNullOrEmpty(domainStr) ? nameStr : $"{domainStr}\\{nameStr}";
        }
        finally
        {
            SidResolverNative.LocalFree(sidPtr);
        }
    }

    private string? TryGetLocalMachineSidPrefix()
    {
        var handle = IntPtr.Zero;
        var buf = IntPtr.Zero;
        try
        {
            var attrs = new SidResolverNative.LSA_OBJECT_ATTRIBUTES();
            var status = SidResolverNative.LsaOpenPolicy(
                IntPtr.Zero, ref attrs, SidResolverNative.PolicyViewLocalInformation, out handle);
            if (status != 0)
            {
                _log.Error($"LsaOpenPolicy failed with status 0x{status:X8}");
                return null;
            }

            status = SidResolverNative.LsaQueryInformationPolicy(
                handle, SidResolverNative.PolicyAccountDomainInformation, out buf);
            if (status != 0 || buf == IntPtr.Zero)
            {
                _log.Error($"LsaQueryInformationPolicy failed with status 0x{status:X8}");
                return null;
            }

            var info = Marshal.PtrToStructure<SidResolverNative.POLICY_ACCOUNT_DOMAIN_INFO>(buf);
            if (info.DomainSid == IntPtr.Zero)
            {
                _log.Error("LsaQueryInformationPolicy returned null DomainSid");
                return null;
            }

            if (!SidResolverNative.ConvertSidToStringSidW(info.DomainSid, out var strPtr))
            {
                _log.Error("ConvertSidToStringSidW failed for local machine SID");
                return null;
            }

            string sidStr;
            try
            {
                sidStr = Marshal.PtrToStringUni(strPtr) ?? string.Empty;
            }
            finally
            {
                SidResolverNative.LocalFree(strPtr);
            }

            if (string.IsNullOrEmpty(sidStr))
            {
                _log.Error("ConvertSidToStringSidW returned empty string");
                return null;
            }

            var prefix = sidStr + "-";
            _log.Info($"Local machine SID prefix: {prefix}");
            return prefix;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to determine local machine SID prefix", ex);
            return null;
        }
        finally
        {
            if (buf != IntPtr.Zero)
                SidResolverNative.LsaFreeMemory(buf);
            if (handle != IntPtr.Zero)
                SidResolverNative.LsaClose(handle);
        }
    }

    private bool IsDomainSid(string sidString)
    {
        if (_localMachineSidPrefix == null)
            return true;
        if (!sidString.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
            return false;
        if (sidString.StartsWith(_localMachineSidPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    public string? TryResolveNameFromRegistry(string sid)
    {
        var path = TryGetProfilePath(sid);
        if (path == null)
            return null;
        var leaf = Path.GetFileName(path);
        return string.IsNullOrEmpty(leaf) ? null : leaf;
    }

    public string? TryGetProfilePath(string sid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"{Constants.ProfileListRegistryKey}\{sid}");
            var raw = key?.GetValue("ProfileImagePath") as string;
            return string.IsNullOrEmpty(raw) ? null : Environment.ExpandEnvironmentVariables(raw);
        }
        catch
        {
            return null;
        }
    }

    public string GetCurrentUserSid() => SidResolutionHelper.GetCurrentUserSid();

    public string? TryGetDesktopPath(string sid, bool isCurrentAccount)
    {
        if (isCurrentAccount)
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        var profilePath = TryGetProfilePath(sid);
        return profilePath == null ? null : Path.Combine(profilePath, "Desktop");
    }

    public string? TryGetStartMenuProgramsPath(string sid, bool isCurrentAccount)
    {
        if (isCurrentAccount)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
        }

        var profilePath = TryGetProfilePath(sid);
        if (profilePath == null)
            return null;
        return Path.Combine(profilePath, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs");
    }

    public string? ResolveSidFromName(string accountName, List<LocalUserAccount>? localUsers)
    {
        var match = localUsers?.FirstOrDefault(u =>
            string.Equals(u.Username, accountName, StringComparison.OrdinalIgnoreCase));
        return match != null ? match.Sid : TryResolveSid(accountName);
    }
}