namespace PrefTrans.Services.IO;

public class FileSystemPinnedShortcutFileStore : IPinnedShortcutFileStore
{
    public IReadOnlyList<string> EnumerateShortcutFiles(string folder)
    {
        if (!Directory.Exists(folder))
            return [];

        return Directory.GetFiles(folder, "*.lnk");
    }

    public byte[] ReadAllBytes(string path)
    {
        return File.ReadAllBytes(path);
    }

    public void WriteAllBytes(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
    }

    public void EnsureDirectory(string folder)
    {
        Directory.CreateDirectory(folder);
    }
}
