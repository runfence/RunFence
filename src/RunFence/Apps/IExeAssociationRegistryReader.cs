namespace RunFence.Apps;

public interface IExeAssociationRegistryReader
{
    /// <summary>
    /// Returns valid association keys (e.g. ".pdf", "http") whose registered command in
    /// the interactive user's HKU or HKLM uses this exe. Results are cached per exe.
    /// </summary>
    IReadOnlyList<string> GetHandledAssociations(string exePath);

    /// <summary>
    /// Returns the non-default arguments from the registry command for this exe+key, or
    /// null if not found, exe doesn't match, or args are trivial ("%1"/empty).
    /// </summary>
    string? GetNonDefaultArguments(string exePath, string key);

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="className"/> is a registered ProgId in
    /// <c>HKLM\Software\Classes\{className}\shell\open\command</c>. Only meaningful for extension
    /// keys (starting with <c>.</c>); returns <see langword="false"/> for protocols.
    /// </summary>
    bool IsRegisteredProgId(string extensionKey, string className);
}
