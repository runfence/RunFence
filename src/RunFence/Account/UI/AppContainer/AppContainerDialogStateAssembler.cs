using RunFence.Account.Lifecycle;
using RunFence.Core.Models;

namespace RunFence.Account.UI.AppContainer;

public class AppContainerDialogStateAssembler
{
    public AppContainerEditSubmitRequest BuildRequest(
        AppContainerEntry? existing,
        string displayName,
        bool isEphemeral,
        IReadOnlyList<string> selectedCapabilities,
        bool loopbackChecked,
        IReadOnlyList<string> comClsids)
    {
        return new AppContainerEditSubmitRequest
        {
            Existing = existing,
            DisplayName = displayName.Trim(),
            ProfileName = BuildProfileName(existing, displayName, isEphemeral),
            IsEphemeral = isEphemeral,
            Capabilities = [.. selectedCapabilities],
            LoopbackChecked = loopbackChecked,
            ComClsids = [.. comClsids],
        };
    }

    internal static string BuildProfileName(
        AppContainerEntry? existing,
        string displayName,
        bool isEphemeral)
    {
        if (existing != null)
            return existing.Name;

        return isEphemeral
            ? "rfn_" + EphemeralNameGenerator.Generate()
            : GenerateProfileName(displayName);
    }

    internal static string GenerateProfileName(string displayName)
    {
        var sanitized = new string(displayName.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray())
            .Trim('_');

        if (sanitized.Length == 0)
            sanitized = "container";

        if (sanitized.Length > 60)
            sanitized = sanitized[..60];

        return "rfn_" + sanitized;
    }
}
