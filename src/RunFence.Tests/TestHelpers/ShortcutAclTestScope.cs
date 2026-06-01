using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace RunFence.Tests.TestHelpers;

public sealed class ShortcutAclTestScope : IDisposable
{
    private readonly TempDirectory _tempDirectory = new("RunFence_ShortcutAcl");
    private int _shortcutIndex;

    public string CreateShortcut(string targetPath, string arguments)
    {
        var shortcutPath = Path.Combine(_tempDirectory.Path, $"shortcut_{_shortcutIndex++}.lnk");
        File.WriteAllText(shortcutPath, $"{targetPath}|{arguments}");
        return shortcutPath;
    }

    public FileSecurity ReadAcl(string shortcutPath)
        => new FileInfo(shortcutPath).GetAccessControl();

    public void Dispose()
    {
        foreach (var filePath in Directory.Exists(_tempDirectory.Path)
                     ? Directory.GetFiles(_tempDirectory.Path, "*", SearchOption.AllDirectories)
                     : [])
        {
            var fileInfo = new FileInfo(filePath);
            var security = fileInfo.GetAccessControl();
            var currentUser = WindowsIdentity.GetCurrent().User!;

            var existingRules = security
                .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToArray();
            foreach (var rule in existingRules)
                security.RemoveAccessRuleSpecific(rule);

            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            fileInfo.SetAccessControl(security);
            File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
        }

        _tempDirectory.Dispose();
    }
}
