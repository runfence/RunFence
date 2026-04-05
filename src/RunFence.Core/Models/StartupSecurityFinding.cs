using System.Security.Cryptography;
using System.Text;

namespace RunFence.Core.Models;

public enum StartupSecurityCategory
{
    StartupFolder,
    RegistryRunKey,
    AutorunExecutable,
    TaskScheduler,
    LogonScript,
    AutoStartService,
    DiskRootAcl,
    AccountPolicy,
    FirewallPolicy
}

public record StartupSecurityFinding(
    StartupSecurityCategory Category,
    string TargetDescription,
    string VulnerableSid,
    string VulnerablePrincipal,
    string AccessDescription,
    string? NavigationTarget = null)
{
    /// <summary>
    /// Returns a stable key that uniquely identifies this finding for suppression tracking.
    /// Uses the same fields as <see cref="ComputeHash"/> (SID, not display name).
    /// </summary>
    public string ComputeKey() => $"{TargetDescription}\0{VulnerableSid}\0{AccessDescription}";

    public static string ComputeHash(List<StartupSecurityFinding> findings)
    {
        if (findings.Count == 0)
            return "";
        var sorted = findings
            .OrderBy(f => f.Category)
            .ThenBy(f => f.TargetDescription, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.VulnerableSid, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.AccessDescription, StringComparer.OrdinalIgnoreCase);
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var f in sorted)
        {
            writer.Write(f.Category);
            writer.Write('\0');
            writer.Write(f.TargetDescription);
            writer.Write('\0');
            writer.Write(f.VulnerableSid);
            writer.Write('\0');
            writer.Write(f.AccessDescription);
            writer.Write('\n');
        }

        writer.Flush();
        stream.Position = 0;
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }
}