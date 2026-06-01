using System.Security.Principal;
using RunFence.Infrastructure;

namespace RunFence.DragBridge;

public sealed class WindowOwnerProcessTokenReader(
    IProcessOwnerSidReader processOwnerSidReader,
    IProcessAppContainerSidReader processAppContainerSidReader,
    IProcessPrivilegeStateReader processPrivilegeStateReader) : IWindowOwnerProcessTokenReader
{
    public bool TryGetTokenInfo(uint processId, out WindowOwnerProcessTokenInfo info)
    {
        var ownerSidValue = processOwnerSidReader.TryGetProcessOwnerSid(processId);
        if (string.IsNullOrWhiteSpace(ownerSidValue))
        {
            info = default;
            return false;
        }

        SecurityIdentifier ownerSid;
        try
        {
            ownerSid = new SecurityIdentifier(ownerSidValue);
        }
        catch (ArgumentException)
        {
            info = default;
            return false;
        }

        SecurityIdentifier? appContainerSid = null;
        var appContainerSidValue = processAppContainerSidReader.TryGetProcessAppContainerSid(processId);
        if (!string.IsNullOrWhiteSpace(appContainerSidValue))
        {
            try
            {
                appContainerSid = new SecurityIdentifier(appContainerSidValue);
            }
            catch (ArgumentException)
            {
                appContainerSid = null;
            }
        }

        int? integrityLevel = processPrivilegeStateReader.TryGetProcessIntegrityLevel(processId, out var level)
            ? level
            : null;
        bool? isElevated = processPrivilegeStateReader.TryGetProcessElevation(processId, out var elevated)
            ? elevated
            : null;
        info = new WindowOwnerProcessTokenInfo(
            ownerSid,
            appContainerSid,
            integrityLevel,
            isElevated);
        return true;
    }
}
