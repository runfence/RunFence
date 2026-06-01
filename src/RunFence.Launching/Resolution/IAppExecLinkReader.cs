namespace RunFence.Launching.Resolution;

public interface IAppExecLinkReader
{
    bool IsAppExecLink(string path);

    bool TryReadStrings(string path, out IReadOnlyList<string> strings);
}
