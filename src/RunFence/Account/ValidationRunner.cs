using RunFence.Core;

namespace RunFence.Account;

/// <summary>
/// Executes a named validation step and captures <see cref="InvalidOperationException"/>
/// messages into an error list instead of propagating them to the caller.
/// Returns <c>true</c> when the step succeeded; <c>false</c> when an error was captured.
/// </summary>
public class ValidationRunner(ILoggingService log)
{
    public bool RunValidation(string stepName, Action action, List<string> errors)
    {
        try
        {
            action();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            log.Warn($"Validation step '{stepName}' failed: {ex.Message}");
            errors.Add(ex.Message);
            return false;
        }
    }
}
