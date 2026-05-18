using System.Runtime.InteropServices;
using RunFence.Launching.Environment;
using Xunit;

namespace RunFence.Tests;

public class EnvironmentBlockTests
{
    [Fact]
    public void Dispose_CallsReleaseExactlyOnce()
    {
        var releaseCount = 0;
        var block = EnvironmentBlock.Own(new IntPtr(1234), _ => releaseCount++);

        block.Dispose();
        block.Dispose();

        Assert.Equal(1, releaseCount);
        Assert.Equal(IntPtr.Zero, block.Pointer);
    }

    [Fact]
    public void Detach_PreventsReleaseOnDispose()
    {
        var releaseCount = 0;
        var block = EnvironmentBlock.Own(BuildBlock(new Dictionary<string, string> { ["PATH"] = @"C:\Base" }), _ => releaseCount++);

        var detached = block.Detach();
        block.Dispose();

        Assert.Equal(IntPtr.Zero, block.Pointer);
        Assert.Equal(0, releaseCount);

        Marshal.FreeHGlobal(detached);
    }

    [Fact]
    public void Empty_DisposeIsNoOp()
    {
        var block = EnvironmentBlock.Empty();

        block.Dispose();

        Assert.Equal(IntPtr.Zero, block.Pointer);
    }

    [Fact]
    public void MergeInPlace_ReplacesExistingBlockAndReleasesOldBlock()
    {
        var releaseCount = 0;
        var originalPointer = BuildBlock(new Dictionary<string, string> { ["PATH"] = @"C:\Base" });
        using var block = EnvironmentBlock.Own(originalPointer, _ => releaseCount++);

        block.MergeInPlace(new Dictionary<string, string>
        {
            ["PATH"] = @"C:\Override",
            ["NEW"] = "VALUE"
        });

        Assert.Equal(1, releaseCount);

        var merged = NativeEnvironmentBlockReader.Read(block.Pointer);
        Assert.Equal(@"C:\Override", merged["PATH"]);
        Assert.Equal("VALUE", merged["NEW"]);
    }

    private static IntPtr BuildBlock(IReadOnlyDictionary<string, string> variables)
    {
        using var block = EnvironmentBlock.Build(variables);
        return block.Detach();
    }
}
