namespace RunFence.Acl;

public interface IAclAccessorNative
{
    bool SetFileSecurity(string path, uint securityInformation, IntPtr securityDescriptor);
}
