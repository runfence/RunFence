using RunFence.Core;

namespace RunFence.Account.UI;

public interface ISecureClipboardService : IDisposable
{
    void CopyProtectedStringToClipboard(ProtectedString password);

    void ScheduleClipboardClear();
}
