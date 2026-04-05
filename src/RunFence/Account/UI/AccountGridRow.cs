using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account.UI;

/// <summary>
/// Discriminated union interface for AccountsPanel grid rows.
/// Implemented by AccountRow (user accounts), ContainerRow (AppContainers), and ProcessRow (running processes).
/// </summary>
public interface IAccountGridRow;

public record AccountGroupHeader;

public class AccountRow : IAccountGridRow
{
    public CredentialEntry? Credential { get; }
    public string Username { get; }
    public string Sid { get; }
    public bool HasStoredPassword { get; }
    public bool IsUnavailable { get; }
    public bool IsEphemeral { get; }

    public bool CanImport => !IsUnavailable &&
                             (HasStoredPassword || SidResolutionHelper.CanLaunchWithoutPassword(Sid));

    public AccountRow(CredentialEntry? credential, string username, string sid, bool hasStoredPassword, bool isUnavailable = false, bool isEphemeral = false)
    {
        Credential = credential;
        Username = username;
        Sid = sid;
        HasStoredPassword = hasStoredPassword;
        IsUnavailable = isUnavailable;
        IsEphemeral = isEphemeral;
    }
}

public class ContainerRow : IAccountGridRow
{
    public AppContainerEntry Container { get; }
    public string ContainerSid { get; }

    public ContainerRow(AppContainerEntry container, string containerSid)
    {
        Container = container;
        ContainerSid = containerSid;
    }
}

public class ProcessRow : IAccountGridRow
{
    public ProcessInfo Process { get; }
    public string OwnerSid { get; }
    public bool IsLast { get; }
    public string DisplayLine { get; }
    public int PidColumnChars { get; }

    public ProcessRow(ProcessInfo process, string ownerSid, bool isLast, string displayLine, int pidColumnChars)
    {
        Process = process;
        OwnerSid = ownerSid;
        IsLast = isLast;
        DisplayLine = displayLine;
        PidColumnChars = pidColumnChars;
    }
}