using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.DragBridge;

public static class DragBridgeLaunchPolicy
{
    public static PrivilegeLevel ResolvePrivilegeLevel(WindowOwnerInfo ownerInfo)
    {
        if (ownerInfo.IsInRestrictedJob)
        {
            return ownerInfo.IntegrityLevel <= NativeTokenHelper.MandatoryLevelLow
                ? PrivilegeLevel.LowIntegrity
                : PrivilegeLevel.Basic;
        }

        if (ownerInfo.IntegrityLevel >= NativeTokenHelper.MandatoryLevelHigh)
            return PrivilegeLevel.HighestAllowed;

        if (ownerInfo.IntegrityLevel >= NativeTokenHelper.MandatoryLevelMedium)
            return PrivilegeLevel.AboveBasic;

        return PrivilegeLevel.HighestAllowed;
    }

    public static bool RequiresLowIntegrityPipe(WindowOwnerInfo ownerInfo) =>
        ResolvePrivilegeLevel(ownerInfo) == PrivilegeLevel.LowIntegrity;
}
