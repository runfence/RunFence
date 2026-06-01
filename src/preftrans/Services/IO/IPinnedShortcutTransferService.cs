using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public interface IPinnedShortcutTransferService
{
    void ReadPinnedShortcuts(TaskbarSettings taskbar);

    bool WritePinnedShortcuts(TaskbarSettings taskbar);
}
