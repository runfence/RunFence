using RunFence.Infrastructure;

namespace RunFence.Acl;

public class AclAccessorNative : IAclAccessorNative
{
    public bool SetFileSecurity(string path, uint securityInformation, IntPtr securityDescriptor) =>
        FileSecurityNative.SetFileSecurity(path, securityInformation, securityDescriptor);
}
