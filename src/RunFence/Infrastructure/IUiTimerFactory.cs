namespace RunFence.Infrastructure;

/// <summary>
/// Creates <see cref="IUiTimer"/> instances.
/// Production implementation wraps <see cref="System.Windows.Forms.Timer"/>;
/// test implementations provide deterministic, synchronous tick control.
/// </summary>
public interface IUiTimerFactory
{
    IUiTimer Create();
}
