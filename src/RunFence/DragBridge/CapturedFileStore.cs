using System.Collections.Immutable;

namespace RunFence.DragBridge;

public readonly record struct CapturedFilesResult(
    IReadOnlyList<string>? Files,
    string? SourceSid,
    string? SourceContainerSid,
    bool Expired);

public interface ICapturedFileStore
{
    void SetCapturedFiles(IReadOnlyList<string> files, string sourceSid, string? sourceContainerSid);
    CapturedFilesResult GetCapturedFiles();
}

public class CapturedFileStore(Func<long> tickCount) : ICapturedFileStore
{
    private readonly Lock _captureLock = new();
    private string? _copySourceSid;
    private string? _copySourceContainerSid;
    private ImmutableArray<string>? _capturedFilePaths;
    private long _captureTicks; // 0 = never set; expiry check guards with _capturedFilePaths != null

    public CapturedFileStore() : this(static () => Environment.TickCount64)
    {
    }

    public void SetCapturedFiles(IReadOnlyList<string> files, string sourceSid, string? sourceContainerSid)
    {
        lock (_captureLock)
        {
            _capturedFilePaths = [..files];
            _copySourceSid = sourceSid;
            _copySourceContainerSid = sourceContainerSid;
            _captureTicks = tickCount();
        }
    }

    public CapturedFilesResult GetCapturedFiles()
    {
        lock (_captureLock)
        {
            var expired = _capturedFilePaths != null && tickCount() - _captureTicks > 5 * 60 * 1000;
            if (expired)
            {
                _capturedFilePaths = null;
                _copySourceSid = null;
                _copySourceContainerSid = null;
            }

            return new CapturedFilesResult(_capturedFilePaths, _copySourceSid, _copySourceContainerSid, expired);
        }
    }
}
