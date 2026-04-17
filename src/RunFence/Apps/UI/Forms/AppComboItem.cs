using RunFence.Core.Models;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Wraps an <see cref="AppEntry"/> for display in a combo box, using the app name as the display string.
/// </summary>
internal sealed class AppComboItem(AppEntry app)
{
    public AppEntry App { get; } = app;
    public override string ToString() => App.Name;
}
