using System.Text.RegularExpressions;

namespace RunFence.Account.UI.AppContainer;

public static class ClsidValidator
{
    private static readonly Regex Pattern = new(
        @"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$",
        RegexOptions.Compiled);

    public static bool IsValid(string? s) => !string.IsNullOrEmpty(s) && Pattern.IsMatch(s);
}