using Moq;
using RunFence.Apps.UI;
using Xunit;

namespace RunFence.Tests;

public class MessageBoxHandlerMappingNotifierTests
{
    [Fact]
    public void ShowAllowPassingArgumentsDisabled_ShowsExpectedMessageBox()
    {
        // Arrange
        var messageBoxService = new Mock<IMessageBoxService>();
        var notifier = new MessageBoxHandlerMappingNotifier(messageBoxService.Object);

        // Act
        notifier.ShowAllowPassingArgumentsDisabled("AppName");

        // Assert
        messageBoxService.Verify(s => s.Show(
            "'Allow Passing Arguments' has been automatically disabled for 'AppName' because no handler mappings remain.",
            "Handler Mappings",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information), Times.Once);
    }
}
