namespace RunFence.Apps.UI;

/// <summary>
/// Row data item for populating the handler mappings grid.
/// </summary>
public record HandlerMappingRowData(
    string Key,
    string HandlerDisplay,
    string AccountDisplay,
    string ArgsTemplate,
    HandlerMappingTag Tag);

/// <summary>
/// Base type for handler mapping row tags, used to identify the mapping in grid rows.
/// </summary>
public abstract record HandlerMappingTag;

/// <summary>
/// Grid row tag identifying an app-based mapping row by its association key and app ID.
/// </summary>
public record AppMappingRowTag(string Key, string AppId,
    IReadOnlyList<string>? PathPrefixes = null, bool ReplacePrefixes = false) : HandlerMappingTag;

/// <summary>
/// Grid row tag identifying a direct handler mapping row by its association key.
/// </summary>
public record DirectHandlerRowTag(string Key) : HandlerMappingTag;
