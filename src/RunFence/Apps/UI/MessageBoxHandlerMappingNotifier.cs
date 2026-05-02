namespace RunFence.Apps.UI;

/// <summary>
/// Default implementation of <see cref="IHandlerMappingNotifier"/> that shows a MessageBox.
/// </summary>
public class MessageBoxHandlerMappingNotifier(IMessageBoxService messageBoxService) : IHandlerMappingNotifier
{
    public void ShowAllowPassingArgumentsEnabled(string appName)
        => messageBoxService.Show(
            $"'Allow Passing Arguments' has been automatically enabled for '{appName}' because handler mappings require argument forwarding.",
            "Handler Mappings", MessageBoxButtons.OK, MessageBoxIcon.Information);

    public void ShowAllowPassingArgumentsDisabled(string appName)
        => messageBoxService.Show(
            $"'Allow Passing Arguments' has been automatically disabled for '{appName}' because no handler mappings remain.",
            "Handler Mappings", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
