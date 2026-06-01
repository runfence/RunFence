using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Core;

namespace RunFence.Account.UI;

public class PackageInstallScriptStore(
    ILoggingService log,
    IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
    IProgramDataObjectProvisioner programDataObjectProvisioner,
    IProgramDataKnownPathResolver programDataKnownPathResolver) : IPackageInstallScriptStore
{
    private readonly string _packageScriptsDir = programDataKnownPathResolver.GetDirectoryPath(
        ProgramDataPolicies.PackageInstallScripts);
    private readonly string _programDataRootPath = Path.GetDirectoryName(
        programDataKnownPathResolver.GetDirectoryPath(ProgramDataPolicies.PackageInstallScripts))!;

    public string CreateScript(string command, string userSid)
    {
        var scriptsDir = programDataDirectoryProvisioningService.EnsureKnownDirectory(
            ProgramDataPolicies.PackageInstallScripts);
        programDataDirectoryProvisioningService.EnsureRoot();
        programDataDirectoryProvisioningService.EnsureTraverseOnlyAccess(
            scriptsDir,
            userSid,
            ProgramDataDirectoryAclProfile.TrustedOnly);

        CleanupStaleScripts();

        var scriptPath = Path.Combine(scriptsDir, $"install-{Guid.NewGuid():N}.ps1");
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
        if (!Directory.Exists(_programDataRootPath))
            return;

        programDataDirectoryProvisioningService.EnsureRoot();
        if (Directory.Exists(_packageScriptsDir))
        {
            programDataDirectoryProvisioningService.EnsureKnownDirectory(
                ProgramDataPolicies.PackageInstallScripts);
        }

        foreach (var stale in GetStaleCandidatePaths())
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
            programDataObjectProvisioner.CreateFile(
                new ProgramDataExplicitFileRequest(
                    filePath,
                    ProgramDataFileAclProfile.TrustedOnly,
                    [CreateFileAccess(
                        userSid,
                        FileSystemRights.ReadAndExecute | FileSystemRights.Delete)],
                    FileShare.Read,
                    OverwriteExisting: false),
                stream =>
                {
                    using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, 1024, leaveOpen: true);
                    writer.Write(command);
                });
        }

        catch (Exception ex)
        {
            log.Error("Failed to create restricted script file", ex);
            Delete(filePath);
            throw new InvalidOperationException("Failed to secure install script", ex);
        }
    }

    private static ProgramDataPrincipalAccess CreateFileAccess(
        string sid,
        FileSystemRights rights)
        => new(
            new SecurityIdentifier(sid),
            rights,
            InheritanceFlags.None,
            PropagationFlags.None);

    private IEnumerable<string> GetStaleCandidatePaths()
    {
        if (Directory.Exists(_programDataRootPath))
        {
            foreach (var stale in Directory.GetFiles(_programDataRootPath, "install-*.ps1"))
                yield return stale;
        }

        if (Directory.Exists(_packageScriptsDir))
        {
            foreach (var stale in Directory.GetFiles(_packageScriptsDir, "install-*.ps1"))
                yield return stale;
        }
    }
}
