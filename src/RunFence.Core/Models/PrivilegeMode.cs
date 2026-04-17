namespace RunFence.Core.Models;

public enum PrivilegeLevel
{
    Basic = 0,          // De-elevate if elevated: strip privileges, set medium integrity. No-op if not elevated.
    HighestAllowed = 1, // Try to elevate via linked token if not already elevated; keep elevated if already elevated
    LowIntegrity = 2,   // De-elevate + set low integrity (elevated tokens); set low integrity only (non-elevated tokens)
}
