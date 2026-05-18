using System;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Infrastructure;

namespace RunFence.Apps;

public class AssociationFallbackRegistry(RegistryKey usersRoot) : IAssociationFallbackRegistry
{
    private readonly RegistryKey _usersRoot = usersRoot ?? throw new ArgumentNullException(nameof(usersRoot));

    public IOwnedAssociationRegistryRoot? OpenUserClassesRoot(string? targetSid = null)
    {
        var sid = targetSid ?? SidResolutionHelper.GetCurrentUserSid();
        var classesRoot = _usersRoot.OpenSubKey($@"{sid}\Software\Classes", writable: true);
        return classesRoot == null ? null : new OwnedAssociationRegistryRoot(classesRoot);
    }

    public string? ReadFallbackCommand(IOwnedAssociationRegistryRoot root, string association)
    {
        using var key = GetClassesRoot(root).OpenSubKey(association, writable: false);
        return key?.GetValue(PathConstants.RunFenceFallbackValueName) as string;
    }

    public void WriteDefaultCommand(IOwnedAssociationRegistryRoot root, string association, string fallbackValue)
    {
        var classesRoot = GetClassesRoot(root);
        if (association.StartsWith('.'))
        {
            using var key = classesRoot.CreateSubKey(association);
            if (!string.IsNullOrEmpty(fallbackValue))
                key.SetValue(null, fallbackValue);
            else
                key.DeleteValue(string.Empty, throwOnMissingValue: false);
            return;
        }

        using var protocolKey = classesRoot.CreateSubKey(association);
        if (!string.IsNullOrEmpty(fallbackValue))
        {
            using var commandKey = classesRoot.CreateSubKey($@"{association}\shell\open\command");
            commandKey.SetValue(null, fallbackValue);
        }
        else
        {
            classesRoot.DeleteSubKeyTree($@"{association}\shell", throwOnMissingSubKey: false);
            protocolKey.DeleteValue("URL Protocol", throwOnMissingValue: false);
        }
    }

    public void DeleteFallbackValue(IOwnedAssociationRegistryRoot root, string association)
    {
        using var key = GetClassesRoot(root).OpenSubKey(association, writable: true);
        key?.DeleteValue(PathConstants.RunFenceFallbackValueName, throwOnMissingValue: false);
    }

    public void DeleteExtensionCommandSubkeys(IOwnedAssociationRegistryRoot root, string association)
    {
        using var extensionKey = GetClassesRoot(root).OpenSubKey(association, writable: true);
        if (extensionKey == null)
            return;

        using (var openKey = extensionKey.OpenSubKey(@"shell\open", writable: true))
            openKey?.DeleteSubKeyTree("command", throwOnMissingSubKey: false);
        using (var openKeyRead = extensionKey.OpenSubKey(@"shell\open"))
        {
            if (openKeyRead is { SubKeyCount: 0, ValueCount: 0 })
            {
                using var shellKey = extensionKey.OpenSubKey("shell", writable: true);
                shellKey?.DeleteSubKey("open", throwOnMissingSubKey: false);
            }
        }
        using (var shellKeyRead = extensionKey.OpenSubKey("shell"))
        {
            if (shellKeyRead is { SubKeyCount: 0, ValueCount: 0 })
                extensionKey.DeleteSubKey("shell", throwOnMissingSubKey: false);
        }
    }

    public void NotifyShellChanged()
    {
        ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    private static RegistryKey GetClassesRoot(IOwnedAssociationRegistryRoot root)
    {
        if (root is not OwnedAssociationRegistryRoot ownedRoot)
            throw new InvalidOperationException($"Unsupported root type: {root.GetType().FullName}");
        return ownedRoot.ClassesRoot;
    }

    private sealed class OwnedAssociationRegistryRoot(RegistryKey classesRoot) : IOwnedAssociationRegistryRoot
    {
        public RegistryKey ClassesRoot { get; } = classesRoot;
        public void Dispose() => ClassesRoot.Dispose();
    }
}
