using System.Text;

namespace RunFence.Infrastructure;

public class FileContentService : IFileContentService
{
    public Stream OpenRead(string path)
        => File.OpenRead(path);

    public string ReadAllText(string path, Encoding encoding)
        => File.ReadAllText(path, encoding);

    public void WriteAllText(string path, string contents, Encoding encoding)
        => File.WriteAllText(path, contents, encoding);
}
