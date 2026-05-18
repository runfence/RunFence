using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Helpers;

namespace RunFence.Launcher;

public class LauncherAssociationFallbackRegistry : IAssociationFallbackRegistry,
    ILauncherAssociationFallbackLookup
{
    public IOwnedAssociationRegistryRoot? OpenUserClassesRoot(string? targetSid = null)
    {
        var classesRoot = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
        return classesRoot == null ? null : new OwnedAssociationRegistryRoot(classesRoot);
    }

    public string? ReadFallbackCommand(IOwnedAssociationRegistryRoot root, string association)
    {
        using var key = GetClassesRoot(root).OpenSubKey(association, writable: false);
        return key?.GetValue(PathConstants.RunFenceFallbackValueName) as string;
    }

    public string? ReadFallbackValue(string association)
    {
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: false);
        if (root == null)
            return null;

        using var key = root.OpenSubKey(association);
        return key?.GetValue(PathConstants.RunFenceFallbackValueName) as string;
    }

    public LauncherFallbackCommandLookupResult ResolveMergedProgIdCommand(string progId)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        if (key == null)
            return LauncherFallbackCommandLookupResult.NotFound();

        var command = key.GetValue(null) as string;
        if (string.IsNullOrEmpty(command))
            return LauncherFallbackCommandLookupResult.NotFound();

        if (AssociationCommandHelper.IsRunFenceLauncherCommand(command))
            return LauncherFallbackCommandLookupResult.RejectedRunFenceCommand();

        return LauncherFallbackCommandLookupResult.Resolved(command);
    }

    public string? ResolveHklmAssociationCommand(string association)
    {
        using var hklmAssociationKey = Registry.LocalMachine.OpenSubKey($@"Software\Classes\{association}");
        if (hklmAssociationKey == null)
            return null;

        string? command;
        if (association.StartsWith('.'))
        {
            var progId = hklmAssociationKey.GetValue(null) as string;
            if (AssociationCommandHelper.IsRunFenceProgId(progId))
                return null;
            if (string.IsNullOrEmpty(progId))
                return null;

            using var progIdCommandKey = Registry.LocalMachine.OpenSubKey($@"Software\Classes\{progId}\shell\open\command");
            command = progIdCommandKey?.GetValue(null) as string;
        }
        else
        {
            using var commandKey = hklmAssociationKey.OpenSubKey(@"shell\open\command");
            command = commandKey?.GetValue(null) as string;
        }

        return !string.IsNullOrWhiteSpace(command) && !AssociationCommandHelper.IsRunFenceLauncherCommand(command)
            ? command
            : null;
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
        OpenFolderNative.SHChangeNotify(OpenFolderNative.SHCNE_ASSOCCHANGED, OpenFolderNative.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
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
