namespace RunFence.Apps.UI;

public static class HandlerAssociationValueNormalization
{
    public static readonly string[] CommonOptions =
        ["Browser (http, https, .htm, .html)", .. AppHandlerRegistrationService.CommonAssociationSuggestions];

    public static string NormalizeKey(string? rawKey) => rawKey?.Trim().ToLowerInvariant() ?? string.Empty;

    public static string? NormalizeTemplate(string? rawTemplate)
        => string.IsNullOrWhiteSpace(rawTemplate) ? null : rawTemplate.Trim();
}
