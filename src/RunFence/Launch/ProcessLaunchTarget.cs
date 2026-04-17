using System.Text;

namespace RunFence.Launch;

/// <summary>
/// Groups the target-process parameters for a launch operation: what runs, with what arguments,
/// where, and with which extra environment variables. Separate from launch credentials and flags.
/// </summary>
/// <param name="Arguments">
/// Raw command-line argument string passed verbatim to the process via
/// <c>ProcessStartInfo.Arguments</c>. This is the exact string the process receives after
/// the runtime's quoting/splitting — no further parsing or re-quoting is applied.
/// Use <see cref="RunFence.Core.CommandLineHelper.JoinArgs"/> to build this from a list when needed.
/// </param>
public record ProcessLaunchTarget(
    string ExePath,
    string? Arguments = null,
    string? WorkingDirectory = null,
    Dictionary<string, string>? EnvironmentVariables = null,
    bool HideWindow = false)
{
    public ProcessLaunchTarget(string ExePath, List<string> ArgumentsList, string? WorkingDirectory = null, Dictionary<string, string>? EnvironmentVariables = null, bool HideWindow = false)
        : this(ExePath, CombineArguments(ArgumentsList), WorkingDirectory, EnvironmentVariables, HideWindow)
    {
    }

    public static string CombineArguments(List<string> args)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (i != 0)
                sb.Append(' ');
            ProcessLaunchHelper.AppendQuotedArg(sb, arg);
        }

        return sb.ToString();
    }
}