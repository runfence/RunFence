using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using RunFence.Core;

namespace RunFence.Firewall;

public class EventLogBlockedConnectionReader(ILoggingService log) : IBlockedConnectionReader
{
    private const string AuditSubcategoryGuid = "{0CCE9226-69AE-11D9-BED3-505054503030}";

    private const int EventId5157 = 5157;

    // Event 5157 property indices
    private const int PropDestAddress = 5;
    private const int PropDestPort = 6;
    private const int PropProtocol = 7;
    private const int PropDirection = 2;
    private const string OutboundDirection = "%%14593";

    public List<BlockedConnection> ReadBlockedConnections(TimeSpan lookback)
    {
        var result = new List<BlockedConnection>();
        var since = DateTime.UtcNow - lookback;

        // XPath filter: Event ID 5157 within the time window
        var query = new EventLogQuery(
            "Security",
            PathType.LogName,
            $"*[System[(EventID={EventId5157}) and TimeCreated[@SystemTime>='{since:yyyy-MM-ddTHH:mm:ss.000Z}']]]");

        try
        {
            using var reader = new EventLogReader(query);
            while (reader.ReadEvent() is { } record)
            {
                using (record)
                {
                    var props = record.Properties;
                    if (props.Count <= PropProtocol)
                        continue;

                    var direction = props[PropDirection].Value?.ToString();
                    if (direction != OutboundDirection)
                        continue;

                    var destAddress = props[PropDestAddress].Value?.ToString();
                    if (string.IsNullOrEmpty(destAddress))
                        continue;

                    if (!int.TryParse(props[PropDestPort].Value?.ToString(), out var destPort))
                        continue;
                    // Protocol parsing serves as a validation gate — events with non-integer
                    // protocol values are malformed and intentionally skipped, not dead code.
                    if (!int.TryParse(props[PropProtocol].Value?.ToString(), out var protocol))
                        continue;

                    result.Add(new BlockedConnection(destAddress, destPort, record.TimeCreated ?? DateTime.UtcNow));
                }
            }
        }
        catch (Exception ex)
        {
            log.Error("BlockedConnectionReader: failed to read Security event log", ex);
        }

        return result;
    }

    public bool IsAuditPolicyEnabled()
    {
        try
        {
            var output = RunAuditPol($"/get /subcategory:{AuditSubcategoryGuid}");
            return output.Contains("Failure", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            log.Error("BlockedConnectionReader: failed to query audit policy", ex);
            return false;
        }
    }

    public void SetAuditPolicyEnabled(bool enabled)
    {
        var failure = enabled ? "enable" : "disable";
        try
        {
            RunAuditPol($"/set /subcategory:{AuditSubcategoryGuid} /failure:{failure}");
        }
        catch (Exception ex)
        {
            log.Error($"BlockedConnectionReader: failed to set audit policy (enabled={enabled})", ex);
            throw;
        }
    }

    private static string RunAuditPol(string args)
    {
        var psi = new ProcessStartInfo("auditpol.exe", args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Failed to start auditpol.exe");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return output;
    }
}