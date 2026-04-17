using System.Security;

namespace PrefTrans.Services;

public class SafeExecutor : ISafeExecutor
{
    public void Try(Action action, string operation)
    {
        try
        {
            action();
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Warning: access denied {operation} setting: {ex.Message}");
        }
        catch (SecurityException ex)
        {
            Console.Error.WriteLine($"Warning: security error {operation} setting: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or FormatException
                                       or OverflowException or ArgumentException)
        {
            Console.Error.WriteLine($"Warning: error {operation} setting: {ex.Message}");
        }
    }
}