using RunFence.Acl;

namespace RunFence.Tests;

internal static class AclAccessorFactory
{
    public static AclAccessor Create() =>
        new(new BackupPrivilegeSecurityDescriptorAccessor(new BackupPrivilegeSecurityNative()));
}
