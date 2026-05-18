using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.DragBridge;

public static class DragBridgeLaunchPolicy
{
    public static PrivilegeLevel ResolvePrivilegeLevel(WindowOwnerInfo ownerInfo)
    {
        if (ownerInfo.IntegrityLevel <= NativeTokenHelper.MandatoryLevelLow)
            return PrivilegeLevel.LowIntegrity;

        if (ownerInfo.IsInRestrictedJob)
            return PrivilegeLevel.Isolated;

        if (ownerInfo.IntegrityLevel >= NativeTokenHelper.MandatoryLevelHigh)
            return PrivilegeLevel.HighestAllowed;

        if (ownerInfo.IntegrityLevel >= NativeTokenHelper.MandatoryLevelMedium)
            return PrivilegeLevel.Basic;

        return PrivilegeLevel.HighestAllowed;
    }

    public static bool RequiresLowIntegrityPipe(WindowOwnerInfo ownerInfo)
        => ownerInfo.IntegrityLevel <= NativeTokenHelper.MandatoryLevelLow;
}
