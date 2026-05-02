namespace RunFence.Core;

public static class EvaluationConstants
{
    public const int MajorVersion = 1;

    public const int EvaluationMaxApps = 3;
    public const int EvaluationMaxContainers = 1;
    public const int EvaluationMaxHiddenAccounts = 1;
    public const int EvaluationMaxCredentials = 3;
    public const int EvaluationMaxFirewallAllowlistEntries = 1;

    /// <summary>Browser-only associations allowed in evaluation mode.</summary>
    public static readonly HashSet<string> BrowserAssociations =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", ".htm", ".html" };
}
