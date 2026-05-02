namespace RunFence.Launch.Tokens;

/// <summary>
/// Creates a de-elevated token using SaferComputeTokenFromLevel(NORMALUSER).
/// </summary>
public interface ISaferDeElevationHelper
{
    /// <summary>
    /// Creates a de-elevated token from <paramref name="hSourceToken"/>.
    /// The caller owns the returned handle and must close it via CloseHandle.
    /// </summary>
    IntPtr CreateDeElevatedToken(IntPtr hSourceToken);
}
