using System.ComponentModel;
using RunFence.Infrastructure;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundPrivilegeMarkerMetadataResolver(
    IProcessImagePathReader processImagePathReader,
    IProcessOwnerSidReader processOwnerSidReader)
{
    public ForegroundPrivilegeMarkerMetadata Resolve(uint processId)
    {
        string processName;
        try
        {
            var processImagePath = processImagePathReader.TryGetProcessImagePath(processId);
            var resolvedProcessName = Path.GetFileName(processImagePath);
            processName = string.IsNullOrWhiteSpace(resolvedProcessName)
                ? ForegroundPrivilegeMarkerMetadata.CreateFallback(processId).ProcessName
                : resolvedProcessName;
        }
        catch (ObjectDisposedException)
        {
            processName = ForegroundPrivilegeMarkerMetadata.CreateFallback(processId).ProcessName;
        }
        catch (InvalidOperationException)
        {
            processName = ForegroundPrivilegeMarkerMetadata.CreateFallback(processId).ProcessName;
        }
        catch (UnauthorizedAccessException)
        {
            processName = ForegroundPrivilegeMarkerMetadata.CreateFallback(processId).ProcessName;
        }
        catch (Win32Exception)
        {
            processName = ForegroundPrivilegeMarkerMetadata.CreateFallback(processId).ProcessName;
        }

        string? accountSid;
        try
        {
            accountSid = processOwnerSidReader.TryGetProcessOwnerSid(processId);
            accountSid = string.IsNullOrWhiteSpace(accountSid)
                ? null
                : accountSid;
        }
        catch (ObjectDisposedException)
        {
            accountSid = null;
        }
        catch (InvalidOperationException)
        {
            accountSid = null;
        }
        catch (UnauthorizedAccessException)
        {
            accountSid = null;
        }
        catch (Win32Exception)
        {
            accountSid = null;
        }

        return new ForegroundPrivilegeMarkerMetadata(processName, accountSid);
    }
}
