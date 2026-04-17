namespace RunFence.Core.Models;

/// <summary>
/// Lightweight per-SID entry in an additional app config file (.rfn).
/// Only contains Sid and Grants — firewall, tray flags, privilege level, and ephemeral settings only live in the main database.
/// </summary>
public class AppConfigAccountEntry
{
    public string Sid { get; set; } = string.Empty;
    public List<GrantedPathEntry> Grants { get; set; } = new();
}