namespace RunFence.Infrastructure;

public interface IKernelObjectMandatoryLabelService
{
    void ApplyLowIntegrityLabel(IntPtr handle);
}
