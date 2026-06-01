using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Core;

namespace RunFence.PrefTrans;

public class PrefTransLogWorkspace(
    ILoggingService log,
    IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
    IProgramDataObjectProvisioner programDataObjectProvisioner,
    IProgramDataKnownPathResolver programDataKnownPathResolver) : IPrefTransLogWorkspace
{
    private readonly string workspace = programDataKnownPathResolver.GetDirectoryPath(
        ProgramDataPolicies.RunFencePrefTransLogs);

    public PrefTransLogWorkspaceResult CreateLogFile(string accountSid)
    {
        try
        {
            programDataDirectoryProvisioningService.EnsureRoot();
            var logWorkspace = programDataDirectoryProvisioningService.EnsureKnownDirectory(
                ProgramDataPolicies.RunFencePrefTransLogs);
            programDataDirectoryProvisioningService.EnsureTraverseOnlyAccess(logWorkspace, accountSid, ProgramDataDirectoryAclProfile.TrustedOnly);
            VerifySecureWorkspace(workspace);
            var logFilePath = Path.Combine(workspace, $"rfn_preftrans_{Guid.NewGuid():N}.log");
            CreateRestrictedLogFile(logFilePath, accountSid);
            return new PrefTransLogWorkspaceResult(true, logFilePath, null);
        }
        catch (Exception ex)
        {
            log.Error("PrefTransLauncher: secure log workspace creation failed", ex);
            return new PrefTransLogWorkspaceResult(false, null, ex.Message);
        }
    }

    public string ReadLogFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void TryDeleteLogFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private void CreateRestrictedLogFile(string path, string accountSid)
        => programDataObjectProvisioner.CreateFile(
            new ProgramDataExplicitFileRequest(
                path,
                ProgramDataFileAclProfile.TrustedOnly,
                [
                    new ProgramDataPrincipalAccess(
                        new SecurityIdentifier(accountSid),
                        FileSystemRights.WriteData |
                        FileSystemRights.AppendData |
                        FileSystemRights.ReadAttributes |
                        FileSystemRights.Synchronize,
                        InheritanceFlags.None,
                        PropagationFlags.None)
                ],
                FileShare.ReadWrite,
                OverwriteExisting: false),
            _ => { });

    private static void VerifySecureWorkspace(string workspacePath)
    {
        var expectedRoot = PathConstants.ProgramDataDir;
        var fullWorkspacePath = Path.GetFullPath(workspacePath);
        var fullExpectedRoot = Path.GetFullPath(expectedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = fullWorkspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(fullExpectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Log workspace is outside the RunFence ProgramData root.");

        var info = new DirectoryInfo(fullWorkspacePath);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Log workspace must not be a reparse point.");
    }
}
