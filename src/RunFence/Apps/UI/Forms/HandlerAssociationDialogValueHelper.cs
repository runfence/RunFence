namespace RunFence.Apps.UI.Forms;

internal sealed class HandlerAssociationDialogValueHelper(IExeAssociationRegistryReader reader)
{
    public const string DefaultArgumentsTemplate = "\"%1\"";

    /// <summary>
    /// Common association key suggestions shown in both app and direct modes.
    /// The first entry is the "Browser" shorthand that expands to http/https/.htm/.html on accept.
    /// </summary>
    public static readonly string[] CommonOptions =
        ["Browser (http, https, .htm, .html)", .. AppHandlerRegistrationService.CommonAssociationSuggestions];

    public static string NormalizeKey(string? rawKey) => rawKey?.Trim().ToLowerInvariant() ?? string.Empty;

    public static string? NormalizeTemplate(string? rawTemplate)
        => string.IsNullOrWhiteSpace(rawTemplate) ? null : rawTemplate.Trim();

    public string ResolveTemplate(string exePath, string key)
    {
        if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(key) || !AppHandlerRegistrationService.IsValidKey(key))
            return DefaultArgumentsTemplate;

        return reader.GetNonDefaultArguments(exePath, key) ?? DefaultArgumentsTemplate;
    }
}
