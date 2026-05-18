using System.Text;

namespace RunFence.Acl;

public sealed class GrantOperationException : Exception
{
    private readonly List<GrantApplyFailure> _cleanupFailures = new();

    public GrantApplyFailureStep Step { get; }
    public string? Path { get; }
    public string? ConfigPath { get; }
    public Exception Cause { get; }

    public IReadOnlyList<GrantApplyFailure> CleanupFailures => _cleanupFailures;

    public GrantOperationException(
        GrantApplyFailureStep step,
        string? path,
        string? configPath,
        Exception cause)
        : this(step, path, configPath, cause, [])
    {
    }

    public GrantOperationException(
        GrantApplyFailureStep step,
        string? path,
        string? configPath,
        Exception cause,
        IEnumerable<GrantApplyFailure> cleanupFailures)
        : base(GrantApplyFailureFormatter.Format(step, path, configPath, cause), cause)
    {
        ArgumentNullException.ThrowIfNull(cause);

        Step = step;
        Path = path;
        ConfigPath = configPath;
        Cause = cause;
        _cleanupFailures.AddRange(cleanupFailures);
    }

    public void AppendCleanupFailure(GrantApplyFailure failure) => _cleanupFailures.Add(failure);

    public void AppendCleanupFailure(GrantApplyFailureStep step, string? path, string? configPath, Exception cause)
    {
        AppendCleanupFailure(new GrantApplyFailure(step, path, configPath, cause));
    }

    public void AppendCleanupFailures(IEnumerable<GrantApplyFailure> failures)
    {
        foreach (var failure in failures)
            AppendCleanupFailure(failure);
    }

    public override string Message => GrantApplyFailureFormatter.Format(Step, Path, ConfigPath, Cause);

    public override string ToString()
    {
        var output = new StringBuilder();
        output.AppendLine("Grant operation failure:");
        output.AppendLine(Message);

        if (_cleanupFailures.Count > 0)
        {
            output.AppendLine("Cleanup failures:");
            foreach (var failure in _cleanupFailures)
            {
                output.Append("  - ");
                output.AppendLine(GrantApplyFailureFormatter.Format(failure));
            }
        }

        output.Append(base.ToString());

        return output.ToString().TrimEnd();
    }
}
