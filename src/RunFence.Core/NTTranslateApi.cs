using System.Diagnostics;
using System.Security.Principal;

namespace RunFence.Core;

public class NTTranslateApi(ILoggingService log)
{
    private const int WarnThresholdMs = 100;

    /// <summary>Translates an account name to a SID. NTAccount creation is inside the timed region.</summary>
    public SecurityIdentifier TranslateSid(string accountName)
    {
        SecurityIdentifier? result = null;
        var sw = Stopwatch.StartNew();
        try
        {
            result = (SecurityIdentifier)new NTAccount(accountName).Translate(typeof(SecurityIdentifier));
            return result;
        }
        finally
        {
            if (sw.ElapsedMilliseconds >= WarnThresholdMs)
                log.Warn($"Slow OS call ({sw.ElapsedMilliseconds}ms): NTTranslate {accountName} → {result?.Value ?? "failed"}");
        }
    }

    /// <summary>Translates a domain\username to a SID. NTAccount creation is inside the timed region.</summary>
    public SecurityIdentifier TranslateSid(string? domain, string username)
    {
        var displayName = string.IsNullOrEmpty(domain) ? username : $"{domain}\\{username}";
        SecurityIdentifier? result = null;
        var sw = Stopwatch.StartNew();
        try
        {
            var account = string.IsNullOrEmpty(domain) ? new NTAccount(username) : new NTAccount(domain, username);
            result = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
            return result;
        }
        finally
        {
            if (sw.ElapsedMilliseconds >= WarnThresholdMs)
                log.Warn($"Slow OS call ({sw.ElapsedMilliseconds}ms): NTTranslate {displayName} → {result?.Value ?? "failed"}");
        }
    }

    /// <summary>Translates a SID string to an NTAccount. SecurityIdentifier creation is inside the timed region.</summary>
    public NTAccount TranslateName(string sidString)
    {
        NTAccount? result = null;
        var sw = Stopwatch.StartNew();
        try
        {
            result = (NTAccount)new SecurityIdentifier(sidString).Translate(typeof(NTAccount));
            return result;
        }
        finally
        {
            if (sw.ElapsedMilliseconds >= WarnThresholdMs)
                log.Warn($"Slow OS call ({sw.ElapsedMilliseconds}ms): NTTranslate {sidString} → {result?.Value ?? "failed"}");
        }
    }
}
