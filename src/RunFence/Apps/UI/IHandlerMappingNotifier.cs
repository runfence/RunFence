namespace RunFence.Apps.UI;

/// <summary>
/// Abstraction for displaying informational notifications during handler mapping mutations.
/// Injected into <see cref="HandlerMappingMutationHandler"/> so notifications can be suppressed in tests.
/// </summary>
public interface IHandlerMappingNotifier
{
    /// <summary>
    /// Notifies the user that 'Allow Passing Arguments' was automatically enabled for the given app.
    /// </summary>
    void ShowAllowPassingArgumentsEnabled(string appName);

    /// <summary>
    /// Notifies the user that 'Allow Passing Arguments' was automatically disabled for the given app.
    /// </summary>
    void ShowAllowPassingArgumentsDisabled(string appName);
}
