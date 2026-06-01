using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public sealed class FolderHandlerCleanupSidSnapshotProvider(
    ILoggingService log,
    ISessionProvider sessionProvider)
{
    public IReadOnlyList<string> Capture()
    {
        try
        {
            var session = sessionProvider.GetSession();
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sid in session.CredentialStore.Credentials
                         .Select(credential => credential.Sid)
                         .Where(sid => !string.IsNullOrWhiteSpace(sid)))
            {
                result.Add(sid);
            }

            foreach (var sid in session.Database.Apps
                         .Select(app => app.AccountSid)
                         .Where(sid => !string.IsNullOrWhiteSpace(sid)))
            {
                result.Add(sid);
            }

            foreach (var sid in session.Database.Accounts
                         .Select(account => account.Sid)
                         .Where(sid => !string.IsNullOrWhiteSpace(sid)))
            {
                result.Add(sid);
            }

            return Array.AsReadOnly(result.ToArray());
        }
        catch (InvalidOperationException ex)
        {
            log.Warn($"FolderHandlerCleanupSidSnapshotProvider: failed to read current session state for cleanup: {ex.Message}");
            return Array.AsReadOnly(Array.Empty<string>());
        }
    }
}
