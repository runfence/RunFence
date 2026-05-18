using RunFence.Account.OrphanedProfiles;
using Xunit;

namespace RunFence.Tests;

public class ProfileSizeCalculatorTests : IDisposable
{
    private readonly TempDirectory _usersDir = new("ProfileSizeCalculator_Test");

    public void Dispose() => _usersDir.Dispose();

    [Fact]
    public void CalculateSizeBytes_ReportsProgressInMegabytesAndReturnsExactBytes()
    {
        var profileDir = Path.Combine(_usersDir.Path, "SizedUser");
        Directory.CreateDirectory(profileDir);
        for (int i = 0; i < 15; i++)
            File.WriteAllBytes(Path.Combine(profileDir, $"part{i}.bin"), new byte[1024 * 1024]);

        var progressValues = new List<long>();
        var calculator = new ProfileSizeCalculator();

        var sizeBytes = calculator.CalculateSizeBytes(
            profileDir,
            new CollectingProgress(progressValues),
            CancellationToken.None);

        Assert.Equal(15L * 1024L * 1024L, sizeBytes);
        Assert.NotEmpty(progressValues);
        Assert.Equal(0, progressValues[0]);
        Assert.Equal(15, progressValues[^1]);
        Assert.True(progressValues.SequenceEqual(progressValues.OrderBy(v => v)));
    }

    [Fact]
    public void CalculateSizeBytes_DoesNotFollowJunctions()
    {
        using var externalDir = new TempDirectory("ProfileSizeJunctionTarget");
        File.WriteAllBytes(Path.Combine(externalDir.Path, "outside.bin"), new byte[5 * 1024 * 1024]);

        var profileDir = Path.Combine(_usersDir.Path, "SizedUser");
        Directory.CreateDirectory(profileDir);
        File.WriteAllBytes(Path.Combine(profileDir, "inside.bin"), new byte[1024 * 1024]);
        JunctionHelper.CreateJunction(Path.Combine(profileDir, "LinkedDir"), externalDir.Path);

        var calculator = new ProfileSizeCalculator();

        var sizeBytes = calculator.CalculateSizeBytes(profileDir, progress: null, CancellationToken.None);

        Assert.Equal(1024L * 1024L, sizeBytes);
    }

    [Fact]
    public void CalculateSizeBytes_MissingDirectory_ReturnsZeroAndReportsZero()
    {
        var missingPath = Path.Combine(_usersDir.Path, "MissingUser");
        var progressValues = new List<long>();
        var calculator = new ProfileSizeCalculator();

        var sizeBytes = calculator.CalculateSizeBytes(
            missingPath,
            new CollectingProgress(progressValues),
            CancellationToken.None);

        Assert.Equal(0, sizeBytes);
        Assert.Equal([0L], progressValues);
    }

    [Fact]
    public void CalculateSizeBytes_PreCancelledToken_ThrowsOperationCanceledException()
    {
        var profileDir = Path.Combine(_usersDir.Path, "CancelledUser");
        Directory.CreateDirectory(profileDir);
        File.WriteAllBytes(Path.Combine(profileDir, "inside.bin"), new byte[1024]);

        var calculator = new ProfileSizeCalculator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            calculator.CalculateSizeBytes(profileDir, progress: null, cts.Token));
    }

    private sealed class CollectingProgress(List<long> values) : IProgress<long>
    {
        public void Report(long value) => values.Add(value);
    }
}
