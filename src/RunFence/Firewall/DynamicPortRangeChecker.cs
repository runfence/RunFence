using System.Text.RegularExpressions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Firewall;

/// <summary>
/// Detects non-standard TCP dynamic port ranges that conflict with RunFence's loopback blocking.
/// The WFP loopback filter exempts ports 49152-65535 (standard ephemeral range). If the system's
/// TCP dynamic port range starts lower (e.g. 1024), OS-assigned ports for loopback socketpairs
/// (curl DNS resolver) and IPC listeners (dotnet test host) fall in the blocked range, causing
/// connection failures. This checker prompts the user to reset the range to standard.
/// </summary>
public partial class DynamicPortRangeChecker(
    ILoggingService log,
    IUserConfirmationService userConfirmation,
    INetshCommandRunner netshCommandRunner)
{
    internal const int StandardEphemeralStart = 49152;
    internal const int StandardEphemeralCount = 16384;

    private bool _dismissed;

    public async Task CheckIfNeededAsync(FirewallAccountSettings settings)
    {
        if (settings.AllowLocalhost || !settings.FilterEphemeralLoopback)
            return;

        try
        {
            if (_dismissed)
                return;

            var (ipv4Start, _) = await ReadIPv4TcpDynamicPortRangeAsync();
            var (ipv6Start, _) = await ReadIPv6TcpDynamicPortRangeAsync();
            var startPort = Math.Min(ipv4Start, ipv6Start);
            if (startPort >= StandardEphemeralStart)
                return;

            var confirmed = userConfirmation.Confirm(
                $"Your system's TCP dynamic port range starts at port {startPort} " +
                $"instead of the standard {StandardEphemeralStart}.\n\n" +
                "This causes loopback communication failures (DNS resolution, test runners, " +
                "inter-process communication) when RunFence's loopback blocking is active.\n\n" +
                $"Reset the TCP dynamic port range to the standard {StandardEphemeralStart}-65535?",
                "RunFence — Network Configuration");

            if (confirmed)
            {
                var ipv4 = await netshCommandRunner.RunAsync($"int ipv4 set dynamicport tcp start={StandardEphemeralStart} num={StandardEphemeralCount}");
                var ipv6 = await netshCommandRunner.RunAsync($"int ipv6 set dynamicport tcp start={StandardEphemeralStart} num={StandardEphemeralCount}");
                if (CommandSucceeded(ipv4) && CommandSucceeded(ipv6))
                {
                    log.Info($"DynamicPortRangeChecker: reset TCP dynamic port range from {startPort} to {StandardEphemeralStart}");
                }
                else
                {
                    log.Warn("DynamicPortRangeChecker: failed to reset TCP dynamic port range to the standard range.");
                }

                return;
            }

            _dismissed = true;
        }
        catch (Exception ex)
        {
            log.Warn($"DynamicPortRangeChecker: check failed: {ex.Message}");
        }
    }

    internal async Task<(int StartPort, int PortCount)> ReadIPv4TcpDynamicPortRangeAsync()
    {
        return ParseCommandResult(await netshCommandRunner.RunAsync("int ipv4 show dynamicport tcp"));
    }

    internal async Task<(int StartPort, int PortCount)> ReadIPv6TcpDynamicPortRangeAsync()
    {
        return ParseCommandResult(await netshCommandRunner.RunAsync("int ipv6 show dynamicport tcp"));
    }

    internal static (int StartPort, int PortCount) ParseDynamicPortRange(string output)
    {
        var matches = PortValueRegex().Matches(output);
        if (matches.Count >= 2 &&
            int.TryParse(matches[0].Groups[1].Value, out var start) &&
            int.TryParse(matches[1].Groups[1].Value, out var count) &&
            start is >= 1 and <= 65535 &&
            count >= 1)
        {
            return (start, count);
        }

        return (StandardEphemeralStart, StandardEphemeralCount);
    }

    private static (int StartPort, int PortCount) ParseCommandResult(DynamicPortRangeCommandResult result)
    {
        if (!CommandSucceeded(result))
            return (StandardEphemeralStart, StandardEphemeralCount);

        return ParseDynamicPortRange(result.StandardOutput);
    }

    private static bool CommandSucceeded(DynamicPortRangeCommandResult result) =>
        !result.TimedOut && result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.FailureMessage);

    [GeneratedRegex(@":\s*(\d+)")]
    private static partial Regex PortValueRegex();
}
