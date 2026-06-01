using Microsoft.Win32.SafeHandles;
using RunFence.Infrastructure;
using RunFence.Ipc;
using Xunit;

namespace RunFence.Tests;

public class OpenFolderValidationLeaseReleaserTests
{
    [Fact]
    public async Task ReleaseAfterSuccessfulOpen_BeforeDelayCompletes_KeepsValidationHandleOpen()
    {
        var delay = new ControlledAsyncDelay();
        var releaser = new OpenFolderValidationLeaseReleaser(delay);
        var tempPath = Path.Combine(Path.GetTempPath(), $"RunFence_OpenFolderLease_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, "lease");
        using SafeFileHandle trackedHandle = File.OpenHandle(tempPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var validation = new DirectoryValidationHandle(trackedHandle)
        {
            IsValid = true,
            CanonicalPath = tempPath
        };

        try
        {
            var releaseTask = releaser.ReleaseAfterSuccessfulOpen(validation);

            Assert.False(releaseTask.IsCompleted);
            Assert.Equal(TimeSpan.FromSeconds(5), delay.RequestedDelay);
            AssertFileDeleteBlocked(tempPath);

            delay.Complete();
            await releaseTask.WaitAsync(TimeSpan.FromSeconds(1));

            File.Delete(tempPath);
        }
        finally
        {
            delay.Complete();
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ReleaseAfterSuccessfulOpen_WhenDelayFaults_DisposesValidationHandleInFinally()
    {
        var delay = new ControlledAsyncDelay();
        var releaser = new OpenFolderValidationLeaseReleaser(delay);
        var tempPath = Path.Combine(Path.GetTempPath(), $"RunFence_OpenFolderLease_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, "lease");
        using SafeFileHandle trackedHandle = File.OpenHandle(tempPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var validation = new DirectoryValidationHandle(trackedHandle)
        {
            IsValid = true,
            CanonicalPath = tempPath
        };
        var delayFailure = new InvalidOperationException("delay failed");

        try
        {
            var releaseTask = releaser.ReleaseAfterSuccessfulOpen(validation);

            Assert.False(releaseTask.IsCompleted);
            AssertFileDeleteBlocked(tempPath);

            delay.Fail(delayFailure);
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => releaseTask);

            Assert.Same(delayFailure, thrown);
            File.Delete(tempPath);
        }
        finally
        {
            delay.Complete();
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static void AssertFileDeleteBlocked(string path)
    {
        Assert.ThrowsAny<IOException>(() => File.Delete(path));
    }

    private sealed class ControlledAsyncDelay : IAsyncDelay
    {
        private readonly TaskCompletionSource completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TimeSpan? RequestedDelay { get; private set; }

        public Task Delay(TimeSpan delay)
        {
            RequestedDelay = delay;
            return completion.Task;
        }

        public void Complete() => completion.TrySetResult();

        public void Fail(Exception ex) => completion.TrySetException(ex);
    }
}
