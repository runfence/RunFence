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

    [JsonIgnore]
    public bool ManageAssociations { get; set; } = true;

    [JsonPropertyName("manageAssociations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    // ReSharper disable once UnusedMember.Global
    public bool? ManageAssociationsSerialized
    {
        get => ManageAssociations ? null : false;
        set => ManageAssociations = value ?? true;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReceiveInjectedInput { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PrivilegeLevel PrivilegeLevel { get; set; } = PrivilegeLevel.Isolated;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? DeleteAfterUtc { get; set; }

    [JsonIgnore]
    public FirewallAccountSettings Firewall { get; set; } = new();

    [JsonPropertyName("firewall")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    // ReSharper disable once UnusedMember.Global
    public FirewallAccountSettings? FirewallSerialized
    {
        get => Firewall.IsDefault ? null : Firewall;
        set => Firewall = value ?? new();
    }

    public List<GrantedPathEntry> Grants { get; set; } = new();

    [JsonIgnore]
    public bool IsEmpty => !IsIpcCaller && !TrayFolderBrowser && !TrayDiscovery && !TrayTerminal
                           && ManageAssociations && !ReceiveInjectedInput
                           && PrivilegeLevel == PrivilegeLevel.Isolated && DeleteAfterUtc == null
                           && Firewall.IsDefault && Grants.Count == 0;

    public AccountEntry Clone() => new()
    {
        Sid = Sid,
        IsIpcCaller = IsIpcCaller,
        TrayFolderBrowser = TrayFolderBrowser,
        TrayDiscovery = TrayDiscovery,
        TrayTerminal = TrayTerminal,
        ManageAssociations = ManageAssociations,
        ReceiveInjectedInput = ReceiveInjectedInput,
        PrivilegeLevel = PrivilegeLevel,
        DeleteAfterUtc = DeleteAfterUtc,
        Firewall = Firewall.Clone(),
        Grants = Grants.Select(g => g.Clone()).ToList()
    };
}
