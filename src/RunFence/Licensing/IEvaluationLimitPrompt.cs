namespace RunFence.Licensing;

/// <summary>
/// Abstraction for displaying evaluation limit messages to the user.
/// Injected into <see cref="EvaluationLimitHelper"/> so the message display can be mocked in tests.
/// </summary>
public interface IEvaluationLimitPrompt
{
    void ShowLimitMessage(string message, IWin32Window? owner);
}
