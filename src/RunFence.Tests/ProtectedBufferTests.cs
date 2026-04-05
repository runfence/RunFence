using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class ProtectedBufferTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(31)]
    public void Constructor_InvalidLength_Throws(int length)
    {
        var data = new byte[length];
        Assert.Throws<ArgumentException>(() => new ProtectedBuffer(data, protect: false));
    }

    [Fact]
    public void Unprotect_ReturnsData()
    {
        var data = new byte[32];
        new Random(42).NextBytes(data);

        using var buffer = new ProtectedBuffer(data, protect: false);
        using var scope = buffer.Unprotect();

        Assert.Same(data, scope.Data);
    }

    [Fact]
    public void Unprotect_WhileAlreadyUnprotected_Throws()
    {
        var data = new byte[32];
        using var buffer = new ProtectedBuffer(data, protect: false);
        using var scope = buffer.Unprotect();

        Assert.Throws<InvalidOperationException>(() => buffer.Unprotect());
    }

    [Fact]
    public void Unprotect_AfterScopeDispose_Succeeds()
    {
        var data = new byte[32];
        new Random(42).NextBytes(data);

        using var buffer = new ProtectedBuffer(data, protect: false);

        // First unprotect/dispose cycle
        var scope1 = buffer.Unprotect();
        scope1.Dispose();

        // Second unprotect should succeed
        using var scope2 = buffer.Unprotect();
        Assert.Same(data, scope2.Data);
    }

    [Fact]
    public void Dispose_ClearsData()
    {
        var data = new byte[32];
        new Random(42).NextBytes(data);

        var buffer = new ProtectedBuffer(data, protect: false);
        buffer.Dispose();

        Assert.All(data, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var data = new byte[32];
        var buffer = new ProtectedBuffer(data, protect: false);

        buffer.Dispose();
        buffer.Dispose(); // Should not throw
    }

    [Fact]
    public void Unprotect_AfterDispose_Throws()
    {
        var data = new byte[32];
        var buffer = new ProtectedBuffer(data, protect: false);
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.Unprotect());
    }
}