using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Account;
using Xunit;

namespace RunFence.Tests;

public class LogonScriptStateRollbackStoreTests : IDisposable
{
    private readonly TempDirectory _tempDir = new("RunFence_LogonScriptRollback");
    private readonly LogonScriptStateRollbackStore _store = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public void Restore_RestoresOriginalBytes()
    {
        var paths = CreatePaths();
        WriteFile(paths.ScriptsIniPath, "original ini");
        WriteFile(paths.GptIniPath, "original gpt");
        WriteFile(paths.WrapperScriptPath, "original wrapper");
        WriteFile(paths.LegacyWrapperScriptPath, "original legacy");
        var snapshot = _store.Capture(paths.ScriptsIniPath, paths.GptIniPath, paths.WrapperScriptPath, paths.LegacyWrapperScriptPath);

        WriteFile(paths.ScriptsIniPath, "mutated ini");
        WriteFile(paths.GptIniPath, "mutated gpt");
        WriteFile(paths.WrapperScriptPath, "mutated wrapper");
        WriteFile(paths.LegacyWrapperScriptPath, "mutated legacy");

        _store.Restore(snapshot);

        Assert.Equal("original ini", File.ReadAllText(paths.ScriptsIniPath));
        Assert.Equal("original gpt", File.ReadAllText(paths.GptIniPath));
        Assert.Equal("original wrapper", File.ReadAllText(paths.WrapperScriptPath));
        Assert.Equal("original legacy", File.ReadAllText(paths.LegacyWrapperScriptPath));
    }

    [Fact]
    public void Restore_RestoresReadOnlyAttributesAfterMutation()
    {
        var paths = CreatePaths();
        WriteFile(paths.WrapperScriptPath, "wrapper");
        File.SetAttributes(paths.WrapperScriptPath, FileAttributes.ReadOnly);
        var snapshot = _store.Capture(paths.ScriptsIniPath, paths.GptIniPath, paths.WrapperScriptPath, paths.LegacyWrapperScriptPath);

        File.SetAttributes(paths.WrapperScriptPath, FileAttributes.Normal);
        WriteFile(paths.WrapperScriptPath, "mutated");

        _store.Restore(snapshot);

        Assert.Equal("wrapper", File.ReadAllText(paths.WrapperScriptPath));
        Assert.True((File.GetAttributes(paths.WrapperScriptPath) & FileAttributes.ReadOnly) != 0);
    }

    [Fact]
    public void Restore_RestoresOriginalFileSecurityDescriptorAfterDaclMutation()
    {
        var paths = CreatePaths();
        WriteFile(paths.WrapperScriptPath, "wrapper");

        var originalSecurity = new FileInfo(paths.WrapperScriptPath).GetAccessControl();
        var originalRules = DescribeRules(originalSecurity);
        var snapshot = _store.Capture(paths.ScriptsIniPath, paths.GptIniPath, paths.WrapperScriptPath, paths.LegacyWrapperScriptPath);

        var mutatedSecurity = new FileSecurity();
        mutatedSecurity.SetSecurityDescriptorSddlForm(
            originalSecurity.GetSecurityDescriptorSddlForm(AccessControlSections.Access),
            AccessControlSections.Access);
        mutatedSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.ReadData,
            AccessControlType.Allow));
        new FileInfo(paths.WrapperScriptPath).SetAccessControl(mutatedSecurity);

        _store.Restore(snapshot);

        var restoredSecurity = new FileInfo(paths.WrapperScriptPath).GetAccessControl();
        Assert.Equal(originalRules, DescribeRules(restoredSecurity));
        Assert.DoesNotContain(DescribeRules(restoredSecurity), rule => rule.Contains("S-1-1-0"));
    }

    [Fact]
    public void Restore_DeletesFilesAbsentAtCaptureTime()
    {
        var paths = CreatePaths();
        var snapshot = _store.Capture(paths.ScriptsIniPath, paths.GptIniPath, paths.WrapperScriptPath, paths.LegacyWrapperScriptPath);

        WriteFile(paths.ScriptsIniPath, "created later");
        WriteFile(paths.GptIniPath, "created later");
        WriteFile(paths.WrapperScriptPath, "created later");
        WriteFile(paths.LegacyWrapperScriptPath, "created later");

        _store.Restore(snapshot);

        Assert.False(File.Exists(paths.ScriptsIniPath));
        Assert.False(File.Exists(paths.GptIniPath));
        Assert.False(File.Exists(paths.WrapperScriptPath));
        Assert.False(File.Exists(paths.LegacyWrapperScriptPath));
    }

    [Fact]
    public void Restore_DeletesReadOnlyFileThatWasAbsentAtCaptureTime()
    {
        var paths = CreatePaths();
        var snapshot = _store.Capture(paths.ScriptsIniPath, paths.GptIniPath, paths.WrapperScriptPath, paths.LegacyWrapperScriptPath);

        WriteFile(paths.WrapperScriptPath, "created later");
        File.SetAttributes(paths.WrapperScriptPath, FileAttributes.ReadOnly);

        _store.Restore(snapshot);

        Assert.False(File.Exists(paths.WrapperScriptPath));
    }

    [Fact]
    public void Restore_RecreatesParentDirectoriesForCapturedFiles()
    {
        var paths = CreatePaths();
        WriteFile(paths.GptIniPath, "gpt");
        var snapshot = _store.Capture(paths.ScriptsIniPath, paths.GptIniPath, paths.WrapperScriptPath, paths.LegacyWrapperScriptPath);

        Directory.Delete(Path.GetDirectoryName(paths.GptIniPath)!, recursive: true);

        _store.Restore(snapshot);

        Assert.True(File.Exists(paths.GptIniPath));
        Assert.Equal("gpt", File.ReadAllText(paths.GptIniPath));
    }

    private (string ScriptsIniPath, string GptIniPath, string WrapperScriptPath, string LegacyWrapperScriptPath) CreatePaths()
    {
        var root = Path.Combine(_tempDir.Path, Guid.NewGuid().ToString("N"));
        return (
            Path.Combine(root, "policy", "scripts.ini"),
            Path.Combine(root, "policy", "gpt.ini"),
            Path.Combine(root, "scripts", "block_login.cmd"),
            Path.Combine(root, "legacy", "block_login.cmd"));
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static IReadOnlyList<string> DescribeRules(FileSecurity security)
    {
        return security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule =>
                $"{rule.IdentityReference.Value}|{rule.AccessControlType}|{rule.FileSystemRights}|{rule.InheritanceFlags}|{rule.PropagationFlags}|{rule.IsInherited}")
            .OrderBy(rule => rule, StringComparer.Ordinal)
            .ToArray();
    }
}
