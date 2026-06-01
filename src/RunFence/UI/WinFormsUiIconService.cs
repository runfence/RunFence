namespace RunFence.UI;

public sealed class WinFormsUiIconService : IUiIconService
{
    public Image CreateToolbarIcon(string text, Color color, int size = 24)
        => UiIconFactory.CreateToolbarIcon(text, color, size);
}
