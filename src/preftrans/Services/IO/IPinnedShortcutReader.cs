namespace PrefTrans.Services.IO;

public interface IPinnedShortcutReader
{
    string? ReadTargetPath(string shortcutPath);
}
