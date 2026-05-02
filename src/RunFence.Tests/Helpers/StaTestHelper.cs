using System.Runtime.ExceptionServices;

namespace RunFence.Tests.Helpers;

/// <summary>
/// Provides a shared helper for running WinForms-related test actions on an STA thread.
/// </summary>
public static class StaTestHelper
{
    /// <summary>
    /// Runs <paramref name="action"/> on a new STA thread and rethrows any exception on the test thread,
    /// preserving the original exception type and stack trace.
    /// </summary>
    public static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        captured?.Throw();
    }
}
