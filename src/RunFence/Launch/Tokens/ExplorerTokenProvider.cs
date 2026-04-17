using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Acquires the interactive user's token from explorer.exe in the current session.
/// SID-verified: only returns a token from an explorer.exe owned by the interactive user,
/// preventing pickup of admin-owned or RunAs-launched explorer processes.
/// </summary>
public class ExplorerTokenProvider(ILoggingService log) : IExplorerTokenProvider
{
    private const uint TokenQuery = ProcessLaunchNative.TOKEN_QUERY;
    private const uint TokenDuplicate = ProcessLaunchNative.TOKEN_DUPLICATE;
    private const uint TokenImpersonate = ProcessLaunchNative.TOKEN_IMPERSONATE;
    private const int TokenUser = 1;

    /// <inheritdoc/>
    public IntPtr TryGetExplorerToken()
    {
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid == null)
            return IntPtr.Zero;

        return TryGetExplorerTokenForSid(interactiveSid);
    }

    /// <inheritdoc/>
    public IntPtr GetExplorerToken()
    {
        var hToken = TryGetExplorerToken();
        if (hToken == IntPtr.Zero)
            throw new InvalidOperationException(
                "Interactive user launch failed: no explorer.exe owned by the interactive user is running in the current session.");
        return hToken;
    }

    /// <inheritdoc/>
    public IntPtr GetSessionExplorerToken()
    {
        var tokens = EnumerateExplorerTokens();
        try
        {
            if (tokens.Count == 0)
                throw new InvalidOperationException(
                    "explorer.exe is not running in the current session. " +
                    "The interactive desktop user's token is required.");

            log.Info($"ExplorerTokenProvider: Acquired session token from explorer.exe (PID {tokens[0].Pid})");
            return tokens[0].Token;
        }
        finally
        {
            for (int i = 1; i < tokens.Count; i++)
                ProcessNative.CloseHandle(tokens[i].Token);
        }
    }

    private IntPtr TryGetExplorerTokenForSid(string targetSid)
    {
        var tokens = EnumerateExplorerTokens();
        IntPtr result = IntPtr.Zero;
        foreach (var (hToken, pid) in tokens)
        {
            if (result != IntPtr.Zero)
            {
                // Already found a match; close all remaining tokens
                ProcessNative.CloseHandle(hToken);
                continue;
            }

            try
            {
                var ownerSid = GetTokenOwnerSid(hToken);
                if (ownerSid != null && string.Equals(ownerSid, targetSid, StringComparison.OrdinalIgnoreCase))
                {
                    log.Info($"ExplorerTokenProvider: Acquired SID-verified token from explorer.exe (PID {pid})");
                    result = hToken;
                    continue;
                }
            }
            catch
            {
                // GetTokenOwnerSid may throw on invalid SID data
            }

            ProcessNative.CloseHandle(hToken);
        }

        return result;
    }

    /// <summary>
    /// Returns open token handles for each explorer.exe process in the current session.
    /// Caller is responsible for closing each token handle via <see cref="ProcessNative.CloseHandle"/>.
    /// Processes that are inaccessible or not in the current session are skipped silently.
    /// </summary>
    private static List<(IntPtr Token, int Pid)> EnumerateExplorerTokens()
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var explorers = Process.GetProcessesByName("explorer");
        var tokens = new List<(IntPtr Token, int Pid)>();
        try
        {
            foreach (var proc in explorers)
            {
                try
                {
                    if (proc.SessionId != currentSessionId)
                        continue;
                    if (ProcessNative.OpenProcessToken(proc.Handle,
                            TokenQuery | TokenDuplicate | TokenImpersonate, out var hToken))
                        tokens.Add((hToken, proc.Id));
                }
                catch
                {
                    /* skip inaccessible process */
                }
            }
        }
        finally
        {
            foreach (var p in explorers)
                p.Dispose();
        }

        return tokens;
    }

    private static string? GetTokenOwnerSid(IntPtr hToken)
    {
        ProcessNative.GetTokenInformation(hToken, TokenUser, IntPtr.Zero, 0, out var needed);
        if (needed <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ProcessNative.GetTokenInformation(hToken, TokenUser, buffer, needed, out _))
                return null;
            var sidPtr = Marshal.ReadIntPtr(buffer);
            return new SecurityIdentifier(sidPtr).Value;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
