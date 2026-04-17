using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account.UI;

/// <summary>
/// Discriminated union interface for AccountsPanel grid rows.
/// Implemented by AccountRow (user accounts), ContainerRow (AppContainers), and ProcessRow (running processes).
/// </summary>
public interface IAccountGridRow;

public record AccountGroupHeader;

public class AccountRow(CredentialEntry? credential, string username, string sid, bool hasStoredPassword, bool isUnavailable = false, bool isEphemeral = false)
    : IAccountGridRow
{
    public CredentialEntry? Credential { get; } = credential;
    public string Username { get; } = username;
    public string Sid { get; } = sid;
    public bool HasStoredPassword { get; } = hasStoredPassword;
    public bool IsUnavailable { get; } = isUnavailable;
    public bool IsEphemeral { get; } = isEphemeral;

    public bool CanImport => !IsUnavailable &&
                             (HasStoredPassword || SidResolutionHelper.CanLaunchWithoutPassword(Sid));
}

public class ContainerRow(AppContainerEntry container, string containerSid) : IAccountGridRow
{
    public AppContainerEntry Container { get; } = container;
    public string ContainerSid { get; } = containerSid;
}

public class ProcessRow(ProcessInfo process, string ownerSid, bool isLast, string displayLine, int pidColumnChars)
    : IAccountGridRow
{
    public ProcessInfo Process { get; } = process;
    public string OwnerSid { get; } = ownerSid;
    public bool IsLast { get; } = isLast;
    public string DisplayLine { get; } = displayLine;
    public int PidColumnChars { get; } = pidColumnChars;
}