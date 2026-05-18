using RunFence.DragBridge;
using Xunit;

namespace RunFence.Tests;

public class DragBridgeChoiceCacheTests
{
    [Fact]
    public void RememberChoice_SameKeyRepeated_DoesNotEvictLiveEntry()
    {
        var cache = new DragBridgeChoiceCache();
        var repeatedPaths = new[] { @"C:\same.txt" };

        for (var i = 0; i < DragBridgeChoiceCache.MaxCapacity + 5; i++)
            cache.RememberChoice("target-a", repeatedPaths, DragBridgeAccessAction.CopyToTemp);

        for (var i = 0; i < DragBridgeChoiceCache.MaxCapacity - 1; i++)
            cache.RememberChoice($"target-{i}", [$@"C:\other-{i}.txt"], DragBridgeAccessAction.CopyToTemp);

        Assert.True(cache.TryGetChoice("target-a", repeatedPaths, out var action));
        Assert.Equal(DragBridgeAccessAction.CopyToTemp, action);
    }
}
