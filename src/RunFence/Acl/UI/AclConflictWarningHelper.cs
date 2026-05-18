namespace RunFence.Acl.UI;

internal static class AclConflictWarningHelper
{
    public const string SpecificContainerAceLowIntegrityConflictMessage =
        "This path has an explicit AppContainer package SID ACE. Specific AppContainer ACEs conflict with ordinary Low Integrity access; remove the specific container ACE or replace it with ALL APPLICATION PACKAGES before adding this Low Integrity grant.";

    public const string LowIntegrityAceSpecificContainerConflictMessage =
        "This path has a Low Integrity ACE. Adding a specific AppContainer package SID ACE here will make ordinary Low Integrity access stop working; remove the Low Integrity grant or use ALL APPLICATION PACKAGES instead.";

    public static string? GetConflictMessage(
        string sid,
        string normalizedPath,
        bool isDeny,
        ISpecificContainerAceConflictDetector detector)
    {
        if (isDeny)
            return null;
        if (AclHelper.IsLowIntegritySid(sid) && detector.HasExplicitSpecificContainerAce(normalizedPath))
            return SpecificContainerAceLowIntegrityConflictMessage;
        if (AclHelper.IsSpecificContainerSid(sid) && detector.HasLowIntegrityAce(normalizedPath))
            return LowIntegrityAceSpecificContainerConflictMessage;
        return null;
    }
}
