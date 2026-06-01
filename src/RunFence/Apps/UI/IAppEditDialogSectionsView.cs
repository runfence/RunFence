namespace RunFence.Apps.UI;

public interface IAppEditDialogSectionsView
{
    IReadOnlyList<HandlerAssociationItem>? GetAssociations();
    void SetAssociations(IReadOnlyList<HandlerAssociationItem>? associations);
    IReadOnlyList<string>? GetPathPrefixes();
    void SetPathPrefixes(IReadOnlyList<string>? prefixes);
    Dictionary<string, string>? GetEnvironmentVariables();
    string? GetFirstDuplicateEnvironmentVariableName();
    void SetEnvironmentEnabled(bool enabled);
    void SetAssociationsEnabled(bool enabled);
    void SetPathPrefixesEnabled(bool enabled);
    void SetHandlerContext(string exePath, string? accountSid);
    void SetPathPrefixTooltip(string? text);
}
