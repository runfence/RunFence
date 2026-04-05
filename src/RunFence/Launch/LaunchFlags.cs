using RunFence.Core.Models;

namespace RunFence.Launch;

public readonly record struct LaunchFlags(
    bool UseSplitToken = false,
    bool UseLowIntegrity = false)
{
    public const string DeElevateTooltip =
        "Strips privileges and sets medium integrity,\nbut Administrators membership cannot be removed.";

    /// <summary>
    /// Resolves account-level split-token and low-integrity defaults from the database.
    /// </summary>
    public static LaunchFlags FromAccountDefaults(AppDatabase db, string accountSid)
    {
        var acct = db.GetAccount(accountSid);
        var useSplitToken = acct?.SplitTokenOptOut != true;
        var useLowIntegrity = acct?.LowIntegrityDefault == true;
        return new LaunchFlags(useSplitToken, useLowIntegrity);
    }
}