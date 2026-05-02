namespace RunFence.Infrastructure;

public sealed class KernelObjectMandatoryLabelService : IKernelObjectMandatoryLabelService
{
    public void ApplyLowIntegrityLabel(IntPtr handle)
    {
        if (!FileSecurityNative.ConvertStringSecurityDescriptorToSecurityDescriptor(
                "S:(ML;;NW;;;LW)",
                1,
                out var sd,
                out _))
        {
            throw new InvalidOperationException("Failed to parse low integrity label descriptor");
        }

        try
        {
            FileSecurityNative.GetSecurityDescriptorSacl(sd, out _, out var sacl, out _);
            var error = FileSecurityNative.SetSecurityInfo(
                handle,
                FileSecurityNative.SE_OBJECT_TYPE.SE_KERNEL_OBJECT,
                FileSecurityNative.SECURITY_INFORMATION.LABEL_SECURITY_INFORMATION,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                sacl);
            if (error != 0)
                throw new InvalidOperationException($"Failed to set low integrity label on kernel object (Win32 error {error})");
        }
        finally
        {
            ProcessNative.LocalFree(sd);
        }
    }
}
