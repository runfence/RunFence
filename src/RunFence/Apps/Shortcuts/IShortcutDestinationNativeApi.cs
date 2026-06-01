using Microsoft.Win32.SafeHandles;
using RunFence.Infrastructure;

namespace RunFence.Apps.Shortcuts;

public interface IShortcutDestinationNativeApi
{
    SafeFileHandle Open(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes);
    FileSecurityNative.BY_HANDLE_FILE_INFORMATION GetFileInformation(SafeFileHandle handle);
    void SetDeleteDisposition(SafeFileHandle handle, FileSecurityNative.FILE_DISPOSITION_FLAGS flags);
    void SetBasicInfo(SafeFileHandle handle, FileSecurityNative.FILE_BASIC_INFO basicInfo);
}
