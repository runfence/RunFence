namespace RunFence.Tests;

/// <summary>
/// Provides an isolated temporary directory for test classes.
/// Creates a GUID-suffixed directory on construction and deletes it on disposal.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory(string prefix = "RunFence_Test")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
        catch
        {
        }
    }
}