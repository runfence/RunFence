using System;
using RunFence.Apps.Shortcuts;
using Xunit;

namespace RunFence.Tests;

public class ExecutableIconCountReaderTests
{
    [Fact]
    public void TryGetIconCount_ReturnsFalseAndZero_WhenNativeCallThrows()
    {
        var reader = new ExecutableIconCountReader(new FakeShortcutIconNativeApi
        {
            OnExtractIconEx = () => throw new UnauthorizedAccessException("No access")
        });

        var result = reader.TryGetIconCount(@"C:\Apps\NoAccess.exe", out var iconCount);

        Assert.False(result);
        Assert.Equal(0, iconCount);
    }

    private sealed class FakeShortcutIconNativeApi : IShortcutIconNativeApi
    {
        public required Func<int> OnExtractIconEx { get; init; }

        public int ExtractIconEx(
            string szFileName,
            int nIconIndex,
            IntPtr[]? phiconLarge,
            IntPtr[]? phiconSmall,
            int nIcons)
            => OnExtractIconEx();
    }
}
