using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

public class AccountEntry
{
    public string Sid { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsIpcCaller { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TrayFolderBrowser { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TrayDiscovery { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TrayTerminal { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LowIntegrityDefault { get; set; }

    /// <summary>
    /// false = default (opted in to split token if admin); true = opted out.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SplitTokenOptOut { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? DeleteAfterUtc { get; set; }

    [JsonIgnore]
    public FirewallAccountSettings Firewall { get; set; } = new();

    [JsonPropertyName("firewall")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FirewallAccountSettings? FirewallSerialized
    {
        get => Firewall.IsDefault ? null : Firewall;
        set => Firewall = value ?? new();
    }

    public List<GrantedPathEntry> Grants { get; set; } = new();

    [JsonIgnore]
    public bool IsEmpty => !IsIpcCaller && !TrayFolderBrowser && !TrayDiscovery && !TrayTerminal
                           && !LowIntegrityDefault && !SplitTokenOptOut && DeleteAfterUtc == null
                           && Firewall.IsDefault && Grants.Count == 0;

    public AccountEntry Clone() => new()
    {
        Sid = Sid,
        IsIpcCaller = IsIpcCaller,
        TrayFolderBrowser = TrayFolderBrowser,
        TrayDiscovery = TrayDiscovery,
        TrayTerminal = TrayTerminal,
        LowIntegrityDefault = LowIntegrityDefault,
        SplitTokenOptOut = SplitTokenOptOut,
        DeleteAfterUtc = DeleteAfterUtc,
        Firewall = Firewall.Clone(),
        Grants = Grants.ToList()
    };
}