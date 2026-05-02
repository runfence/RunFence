using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;

namespace RunFence.Startup;

public class SingleInstanceService : ISingleInstanceService
{
    private Mutex? _mutex;

    public bool TryAcquire()
    {
#pragma warning disable CS0162 // Unreachable code: IsDebugBuild is const true in Debug builds
        if (DebugHelper.IsDebugBuild)
            return true;

        var mutexName = IpcConstants.MutexName;

        var security = new MutexSecurity();
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new MutexAccessRule(
            adminSid,
            MutexRights.FullControl,
            AccessControlType.Allow));

        _mutex = MutexAcl.Create(false, mutexName, out bool createdNew, security);

        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_mutex != null)
        {
            _mutex.Dispose();
            _mutex = null;
        }
    }
}