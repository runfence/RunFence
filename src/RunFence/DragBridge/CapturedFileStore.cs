namespace RunFence.DragBridge;

public readonly record struct CapturedFilesResult(List<string>? Files, string? SourceSid, bool Expired);

public interface ICapturedFileStore
{
    void SetCapturedFiles(List<string> files, string sourceSid);
    CapturedFilesResult GetCapturedFiles();
}

public class CapturedFileStore : ICapturedFileStore
{
    private readonly Func<long> _tickCount;
    private readonly Lock _captureLock = new();
    private string? _copySourceSid;
    private List<string>? _capturedFilePaths;
    private long _captureTicks; // 0 = never set; expiry check guards with _capturedFilePaths != null

    public CapturedFileStore() : this(static () => Environment.TickCount64)
    {
    }

    public CapturedFileStore(Func<long> tickCount)
    {
        _tickCount = tickCount;
    }

    public void SetCapturedFiles(List<string> files, string sourceSid)
    {
        lock (_captureLock)
        {
            _capturedFilePaths = files;
            _copySourceSid = sourceSid;
            _captureTicks = _tickCount();
        }
    }

    public CapturedFilesResult GetCapturedFiles()
    {
        lock (_captureLock)
        {
            var expired = _capturedFilePaths != null && _tickCount() - _captureTicks > 5 * 60 * 1000;
            if (expired)
            {
                _capturedFilePaths = null;
                _copySourceSid = null;
            }

            return new CapturedFilesResult(_capturedFilePaths, _copySourceSid, expired);
        }
    }
}