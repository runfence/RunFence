using RunFence.Acl.UI;

namespace RunFence.RunAs;

public interface IRunAsPermissionPromptHelper
{
    AncestorPermissionResult? PromptIfNeeded(string filePath, string accountSid);
}
