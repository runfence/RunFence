namespace PrefTrans.Services.IO;

public interface IPinnedShortcutFileStore
{
    IReadOnlyList<string> EnumerateShortcutFiles(string folder);

    byte[] ReadAllBytes(string path);

    void WriteAllBytes(string path, byte[] bytes);

    void EnsureDirectory(string folder);
}
