using System.Text;

namespace RunFence.Infrastructure;

public interface IFileContentService
{
    Stream OpenRead(string path);

    string ReadAllText(string path, Encoding encoding);

    void WriteAllText(string path, string contents, Encoding encoding);
}
