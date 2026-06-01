using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public sealed class TokenPrivilegeStateReaderTests
{
    [Fact]
    public void TryGetIntegrityLevel_CurrentProcessToken_ReturnsIntegrityRid()
    {
        var reader = new TokenPrivilegeStateReader();
        var token = OpenCurrentProcessToken();

        try
        {
            var result = reader.TryGetIntegrityLevel(token, out var integrityLevel);

            Assert.True(result);
            Assert.True(integrityLevel >= NativeTokenHelper.MandatoryLevelLow);
        }
        finally
        {
            ProcessNative.CloseHandle(token);
        }
    }

    [Fact]
    public void TryGetIntegrityLevel_InvalidToken_ReturnsFalse()
    {
        var reader = new TokenPrivilegeStateReader();

        var result = reader.TryGetIntegrityLevel(IntPtr.Zero, out var integrityLevel);

        Assert.False(result);
        Assert.Equal(0, integrityLevel);
    }

    [Fact]
    public void IsElevated_InvalidToken_ReturnsFalse()
    {
        var reader = new TokenPrivilegeStateReader();

        var result = reader.IsElevated(IntPtr.Zero);

        Assert.False(result);
    }

    private static IntPtr OpenCurrentProcessToken()
    {
        if (!ProcessNative.OpenProcessToken(
                ProcessNative.GetCurrentProcess(),
                ProcessLaunchNative.TOKEN_QUERY,
                out var token))
        {
            throw new InvalidOperationException("Unable to open current process token for test setup.");
        }

        return token;
    }
}
