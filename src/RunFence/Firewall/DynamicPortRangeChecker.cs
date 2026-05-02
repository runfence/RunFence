using System.Diagnostics;
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
public partial class DynamicPortRangeChecker(ILoggingService log, IUserConfirmationService userConfirmation)
{
    internal const int StandardEphemeralStart = 49152;
    internal const int StandardEphemeralCount = 16384;

    private bool _dismissed;

    /// <summary>
    /// Checks the system's TCP dynamic port range and offers to reset it if non-standard.
    /// Only prompts when <paramref name="settings"/> has loopback blocking with ephemeral filtering.
    /// No-ops after the user declines once per session. Exceptions are caught internally.
    /// </summary>
    public async Task CheckIfNeededAsync(FirewallAccountSettings settings)
    {
        if (settings.AllowLocalhost || !settings.FilterEphemeralLoopback)
            return;
        try { await CheckAndOfferResetAsync(); }
        catch (Exception ex) { log.Warn($"DynamicPortRangeChecker: check failed: {ex.Message}"); }
    }

    /// <summary>
    /// Checks the system's TCP dynamic port range and offers to reset it if non-standard.
    /// No-ops after the user declines once per session.
    /// </summary>
    private async Task CheckAndOfferResetAsync()
    {
        if (_dismissed)
            return;

        var (ipv4Start, _) = await Task.Run(ReadIPv4TcpDynamicPortRange);
        var (ipv6Start, _) = await Task.Run(ReadIPv6TcpDynamicPortRange);
        var startPort = Math.Min(ipv4Start, ipv6Start);
        if (startPort >= StandardEphemeralStart)
            return;

        bool confirmed = userConfirmation.Confirm(
            $"Your system's TCP dynamic port range starts at port {startPort} " +
            $"instead of the standard {StandardEphemeralStart}.\n\n" +
            "This causes loopback communication failures (DNS resolution, test runners, " +
            "inter-process communication) when RunFence's loopback blocking is active.\n\n" +
            $"Reset the TCP dynamic port range to the standard {StandardEphemeralStart}-65535?",
            "RunFence \u2014 Network Configuration");

        if (confirmed)
        {
            await Task.Run(ResetToStandard);
            log.Info($"DynamicPortRangeChecker: reset TCP dynamic port range from {startPort} " +
                     $"to {StandardEphemeralStart}");
        }
        else
        {
            _dismissed = true;
        }
    }

    internal static (int StartPort, int PortCount) ReadIPv4TcpDynamicPortRange()
    {
        var output = RunNetsh("int ipv4 show dynamicport tcp");
        return ParseDynamicPortRange(output);
    }

    internal static (int StartPort, int PortCount) ReadIPv6TcpDynamicPortRange()
    {
        var output = RunNetsh("int ipv6 show dynamicport tcp");
        return ParseDynamicPortRange(output);
    }

    internal static (int StartPort, int PortCount) ParseDynamicPortRange(string output)
    {
        // Extract all numbers after ':' — locale-independent structure:
        //   <localized label> : <number>
        //   <localized label> : <number>
        var matches = PortValueRegex().Matches(output);
        if (matches.Count >= 2 &&
            int.TryParse(matches[0].Groups[1].Value, out var start) &&
            int.TryParse(matches[1].Groups[1].Value, out var count) &&
            start is >= 1 and <= 65535 && count >= 1)
        {
            return (start, count);
        }

        return (StandardEphemeralStart, StandardEphemeralCount);
    }

    private static void ResetToStandard()
    {
        RunNetsh($"int ipv4 set dynamicport tcp start={StandardEphemeralStart} num={StandardEphemeralCount}");
        RunNetsh($"int ipv6 set dynamicport tcp start={StandardEphemeralStart} num={StandardEphemeralCount}");
    }

    private static string RunNetsh(string arguments)
    {
        var proc = Process.Start(new ProcessStartInfo("netsh", arguments)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        // ReadToEndAsync avoids deadlock when output exceeds the internal pipe buffer.
        // A synchronous ReadToEnd followed by WaitForExit can deadlock when the child process
        // fills the pipe buffer waiting for the reader, while the reader is blocked waiting
        // for the process to exit. ReadToEndAsync drains the pipe without blocking the thread.
        var readTask = proc.StandardOutput.ReadToEndAsync();
        if (!readTask.Wait(5000))
        {
            proc.Kill();
            proc.WaitForExit();
            proc.Dispose();
            return string.Empty;
        }

        proc.WaitForExit();
        var result = readTask.Result;
        proc.Dispose();
        return result;
    }

    [GeneratedRegex(@":\s*(\d+)")]
    private static partial Regex PortValueRegex();
}
