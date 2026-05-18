namespace RunFence.Apps.Shortcuts;

public sealed class ShortcutEnforcementException : Exception
{
    public ShortcutEnforcementException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Causes = innerException == null ? [] : [innerException];
    }

    public ShortcutEnforcementException(string message, IReadOnlyList<Exception> causes)
        : base(message, causes.FirstOrDefault())
    {
        Causes = causes;
    }

    public IReadOnlyList<Exception> Causes { get; }
}
