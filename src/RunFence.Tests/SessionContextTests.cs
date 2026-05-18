using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class SessionContextTests
{
    [Fact]
    public void ReplacePinDerivedKey_DisposesOldOwnedKeyAndDoesNotRetargetCapturedSource()
    {
        using var session = new SessionContext()
            .WithOwnedPinDerivedKey(TestSecretFactory.Create(32));

        using var firstKey = new SecureSecret(32, data => data.Fill(1));
        session.ReplacePinDerivedKey(firstKey);
        var capturedSource = session.PinDerivedKey;

        using var secondKey = new SecureSecret(32, data => data.Fill(2));
        session.ReplacePinDerivedKey(secondKey);

        Assert.Throws<ObjectDisposedException>(() => capturedSource.UseSnapshot(_ => { }));
        var current = session.PinDerivedKey.TransformSnapshot(data => data.ToArray());
        Assert.All(current, value => Assert.Equal((byte)2, value));
    }

    [Fact]
    public void ReplacePinDerivedKey_CurrentSourceCannotBeCastToSecureSecret()
    {
        using var session = new SessionContext()
            .WithOwnedPinDerivedKey(TestSecretFactory.Create(32));

        using var key = new SecureSecret(32, data => data.Fill(3));
        session.ReplacePinDerivedKey(key);

        Assert.False(session.PinDerivedKey is SecureSecret);
    }

    [Fact]
    public void ReplacePinDerivedKey_SwitchesCurrentSourceWithoutRetargetingCapturedSource()
    {
        using var session = new SessionContext()
            .WithOwnedPinDerivedKey(TestSecretFactory.Create(32));

        using var firstReplacement = new SecureSecret(32, data => data.Fill(7));
        session.ReplacePinDerivedKey(firstReplacement);
        var capturedSource = session.PinDerivedKey;

        using var secondReplacement = new SecureSecret(32, data => data.Fill(8));
        session.ReplacePinDerivedKey(secondReplacement);

        Assert.False(session.PinDerivedKey is SecureSecret);
        Assert.Throws<ObjectDisposedException>(() => capturedSource.UseSnapshot(_ => { }));
        Assert.False(session.PinDerivedKey is SecureSecret);
        var current = session.PinDerivedKey.TransformSnapshot(data => data.ToArray());
        Assert.All(current, value => Assert.Equal((byte)8, value));
    }
}
