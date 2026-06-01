namespace RunFence.Apps.UI.Forms;

internal sealed class HandlerAssociationDialogValueHelper(IExeAssociationRegistryReader reader)
{
    public const string DefaultArgumentsTemplate = "\"%1\"";

    /// <summary>
    /// Common association key suggestions shown in both app and direct modes.
    /// The first entry is the "Browser" shorthand that expands to http/https/.htm/.html on accept.
    /// </summary>
    public static readonly string[] CommonOptions = HandlerAssociationValueNormalization.CommonOptions;

    public static string NormalizeKey(string? rawKey) => HandlerAssociationValueNormalization.NormalizeKey(rawKey);

    public static string? NormalizeTemplate(string? rawTemplate)
        => HandlerAssociationValueNormalization.NormalizeTemplate(rawTemplate);

    public string ResolveTemplate(string exePath, string key, string? accountSid = null)
    {
        if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(key) || !AppHandlerRegistrationService.IsValidKey(key))
            return DefaultArgumentsTemplate;

        return reader.GetNonDefaultArguments(exePath, key, accountSid) ?? DefaultArgumentsTemplate;
    }
}
