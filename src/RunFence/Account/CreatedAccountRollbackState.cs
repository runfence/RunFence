using RunFence.Core.Models;

namespace RunFence.Account;

public sealed class CreatedAccountRollbackState
{
    public required string Sid { get; init; }
    public required string Username { get; init; }
    public Guid? CredentialId { get; set; }
    public AccountEntry? PreviousAccount { get; init; }
    public bool HadPreviousAccount { get; init; }
    public string? PreviousSidName { get; init; }
    public bool HadPreviousSidName { get; init; }
    public FirewallAccountSettings? PreviousFirewallSettings { get; init; }
    public bool HadPreviousFirewallSettings { get; init; }
}
