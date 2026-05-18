namespace RunFence.Infrastructure;

public sealed class NoOpTrayWarningSink : ITrayWarningSink
{
    public void ShowWarning(string text)
    {
    }
}
