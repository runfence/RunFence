using System.Windows.Forms;
using Moq;
using RunFence.Core;
using RunFence.RunAs.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class RunAsAdHocPasswordPromptServiceTests
{
    [Fact]
    public void Prompt_WhenRememberPasswordIsDisallowed_ClearsRememberFlag()
    {
        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());
        var adapter = new Mock<IRunAsPasswordDialogAdapter>();
        adapter.SetupGet(a => a.Password).Returns(password);
        adapter.SetupGet(a => a.RememberPassword).Returns(true);
        adapter.Setup(a => a.ShowDialog(It.IsAny<IWin32Window?>())).Returns(DialogResult.OK);

        var factory = new Mock<IRunAsPasswordDialogAdapterFactory>();
        factory
            .Setup(f => f.Create("Demo", false, "S-1-5-21-1", "demo"))
            .Returns(adapter.Object);

        var service = new RunAsAdHocPasswordPromptService(factory.Object);

        var result = service.Prompt(null, "S-1-5-21-1", "demo", "Demo", allowRememberPassword: false);

        Assert.True(result.Accepted);
        Assert.False(result.RememberPassword);
    }

    [Fact]
    public void Prompt_WhenCancelled_ReturnsDeclinedResult()
    {
        var adapter = new Mock<IRunAsPasswordDialogAdapter>();
        adapter.Setup(a => a.ShowDialog(It.IsAny<IWin32Window?>())).Returns(DialogResult.Cancel);

        var factory = new Mock<IRunAsPasswordDialogAdapterFactory>();
        factory
            .Setup(f => f.Create("Demo", true, "S-1-5-21-1", "demo"))
            .Returns(adapter.Object);

        var service = new RunAsAdHocPasswordPromptService(factory.Object);

        var result = service.Prompt(null, "S-1-5-21-1", "demo", "Demo", allowRememberPassword: true);

        Assert.False(result.Accepted);
        Assert.Null(result.Password);
        Assert.False(result.RememberPassword);
    }
}
