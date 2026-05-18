using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;

namespace RunFence.Account.UI;

public class PackageInstallScriptStore(ILoggingService log) : IPackageInstallScriptStore
{
    public string CreateScript(string command, string userSid)
    {
        var dir = PathConstants.ProgramDataDir;
        Directory.CreateDirectory(dir);

        CleanupStaleScripts();

        var scriptPath = Path.Combine(dir, $"install-{Guid.NewGuid():N}.ps1");
        CreateScriptFileWithRestrictedAccess(scriptPath, command, userSid);
        return scriptPath;
    }

    public void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    public void CleanupStaleScripts()
    {
        var dir = PathConstants.ProgramDataDir;
        if (!Directory.Exists(dir))
            return;

        foreach (var stale in Directory.GetFiles(dir, "install-*.ps1"))
        {
            try
            {
                if (File.GetCreationTimeUtc(stale) > DateTime.UtcNow.AddHours(-1))
                    continue;

                File.Delete(stale);
            }
            catch
            {
            }
        }
    }

    private void CreateScriptFileWithRestrictedAccess(string filePath, string command, string userSid)
    {
        try
        {
            using var fs = CreateRestrictedFile(
                filePath,
                userSid,
                FileSystemRights.ReadAndExecute | FileSystemRights.Delete);
            using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
            writer.Write(command);
        }
        catch (Exception ex)
        {
            log.Error("Failed to create restricted script file", ex);
            Delete(filePath);
            throw new InvalidOperationException("Failed to secure install script", ex);
        }
    }

    private FileStream CreateRestrictedFile(string filePath, string userSid, FileSystemRights userRights)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);

        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            administrators, FileSystemRights.FullControl, AccessControlType.Allow));
        AdminOperationMockAccessHelper.AddCurrentProcessFileSystemAccess(
            security,
            FileSystemRights.FullControl);

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            system, FileSystemRights.FullControl, AccessControlType.Allow));

        var user = new SecurityIdentifier(userSid);
        security.AddAccessRule(new FileSystemAccessRule(
            user, userRights, AccessControlType.Allow));

        var fileInfo = new FileInfo(filePath);
        return fileInfo.Create(
            FileMode.CreateNew,
            FileSystemRights.Write | FileSystemRights.ReadData,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.None,
            security);
    }
}
