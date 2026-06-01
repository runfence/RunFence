using Microsoft.Win32.SafeHandles;

namespace RunFence.Infrastructure;

public interface IProcessQueryHandleProvider
{
    bool TryOpenProcessForQuery(uint processId, out SafeProcessHandle processHandle);
}
