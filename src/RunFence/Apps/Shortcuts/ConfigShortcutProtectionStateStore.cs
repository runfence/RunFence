using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Apps.Shortcuts;

public sealed class ConfigShortcutProtectionStateStore(
    ISessionProvider sessionProvider,
    IAppConfigService appConfigService,
    Func<IUiThreadInvoker> uiThreadInvokerFactory) : IShortcutProtectionStateStore
{
    public ShortcutProtectionState? Load(string appId, string shortcutPath)
    {
        var normalizedPath = NormalizePath(shortcutPath);
        return InvokeOnUiThread(() =>
        {
            var session = sessionProvider.GetSession();
            var app = FindApp(session.Database, appId);
            if (app == null || app.ShortcutProtectionStates == null)
                return null;

            return app.ShortcutProtectionStates.FirstOrDefault(state =>
                TryNormalizePath(state.ShortcutPath, out var statePath) &&
                string.Equals(statePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        });
    }

    public void Save(string appId, ShortcutProtectionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var normalizedPath = NormalizePath(state.ShortcutPath);

        InvokeOnUiThread(() =>
        {
            var session = sessionProvider.GetSession();
            var app = FindApp(session.Database, appId)
                ?? throw new InvalidOperationException($"Application '{appId}' was not found.");
            var originalStates = app.ShortcutProtectionStates?.ToList();
            var updatedStates = originalStates?.ToList() ?? [];
            var existingIndexes = FindStateIndexes(updatedStates, normalizedPath);
            var updated = state with { ShortcutPath = normalizedPath };

            var changed = existingIndexes.Count == 0 ||
                          existingIndexes.Count > 1 ||
                          !Equals(updatedStates[existingIndexes[0]], updated);
            if (!changed)
                return;

            RemoveStateIndexesDescending(updatedStates, existingIndexes);
            updatedStates.Add(updated);

            app.ShortcutProtectionStates = updatedStates;

            try
            {
                appConfigService.SaveConfigForApp(
                    appId,
                    session.Database,
                    session.PinDerivedKey,
                    session.CredentialStore.ArgonSalt);
            }
            catch
            {
                app.ShortcutProtectionStates = NormalizeStates(originalStates);
                throw;
            }

        });
    }

    public void Delete(string appId, string shortcutPath)
    {
        var normalizedPath = NormalizePath(shortcutPath);

        InvokeOnUiThread(() =>
        {
            var session = sessionProvider.GetSession();
            var app = FindApp(session.Database, appId);
            if (app?.ShortcutProtectionStates == null)
                return;

            var originalStates = app.ShortcutProtectionStates.ToList();
            var updatedStates = originalStates.ToList();
            var existingIndexes = FindStateIndexes(updatedStates, normalizedPath);
            if (existingIndexes.Count == 0)
                return;

            RemoveStateIndexesDescending(updatedStates, existingIndexes);
            app.ShortcutProtectionStates = NormalizeStates(updatedStates);

            try
            {
                appConfigService.SaveConfigForApp(
                    appId,
                    session.Database,
                    session.PinDerivedKey,
                    session.CredentialStore.ArgonSalt);
            }
            catch
            {
                app.ShortcutProtectionStates = NormalizeStates(originalStates);
                throw;
            }

        });
    }

    public void PruneMissingFiles(string appId)
    {
        List<ShortcutProtectionState> states = InvokeOnUiThread(() =>
        {
            var session = sessionProvider.GetSession();
            var app = FindApp(session.Database, appId);
            return app?.ShortcutProtectionStates?.ToList() ?? [];
        });
        if (states.Count == 0)
            return;

        var missingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in states)
        {
            if (!TryNormalizePath(state.ShortcutPath, out var normalizedPath))
            {
                missingPaths.Add(state.ShortcutPath);
                continue;
            }

            if (TryProbeMissingFile(normalizedPath, out var isMissing) && isMissing)
            {
                missingPaths.Add(normalizedPath);
            }
        }

        if (missingPaths.Count == 0)
            return;

        InvokeOnUiThread(() =>
        {
            var session = sessionProvider.GetSession();
            var app = FindApp(session.Database, appId);
            if (app?.ShortcutProtectionStates == null)
                return;

            var originalStates = app.ShortcutProtectionStates.ToList();
            var updatedStates = originalStates
                .Where(state =>
                {
                    if (!TryNormalizePath(state.ShortcutPath, out var normalizedPath))
                        return false;
                    return !missingPaths.Contains(normalizedPath) && !missingPaths.Contains(state.ShortcutPath);
                })
                .ToList();

            if (updatedStates.Count == originalStates.Count)
                return;

            app.ShortcutProtectionStates = NormalizeStates(updatedStates);
            try
            {
                appConfigService.SaveConfigForApp(
                    appId,
                    session.Database,
                    session.PinDerivedKey,
                    session.CredentialStore.ArgonSalt);
            }
            catch
            {
                app.ShortcutProtectionStates = NormalizeStates(originalStates);
                throw;
            }

        });
    }

    private static AppEntry? FindApp(AppDatabase database, string appId)
        => database.Apps.FirstOrDefault(app => string.Equals(app.Id, appId, StringComparison.Ordinal));

    private static List<int> FindStateIndexes(IReadOnlyList<ShortcutProtectionState> states, string normalizedPath)
    {
        var indexes = new List<int>();
        for (var i = 0; i < states.Count; i++)
        {
            if (TryNormalizePath(states[i].ShortcutPath, out var statePath) &&
                string.Equals(statePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                indexes.Add(i);
            }
        }

        return indexes;
    }

    private static List<ShortcutProtectionState>? NormalizeStates(List<ShortcutProtectionState>? states)
        => states is { Count: > 0 } ? states : null;

    private static void RemoveStateIndexesDescending(List<ShortcutProtectionState> states, List<int> indexes)
    {
        for (var i = indexes.Count - 1; i >= 0; i--)
            states.RemoveAt(indexes[i]);
    }

    private T InvokeOnUiThread<T>(Func<T> action)
        => uiThreadInvokerFactory().Invoke(action);

    private void InvokeOnUiThread(Action action)
        => uiThreadInvokerFactory().Invoke(action);

    private static string NormalizePath(string path)
        => Path.GetFullPath(path);

    private static bool TryNormalizePath(string path, out string normalizedPath)
    {
        try
        {
            normalizedPath = NormalizePath(path);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (PathTooLongException)
        {
        }

        normalizedPath = string.Empty;
        return false;
    }

    private static bool TryProbeMissingFile(string normalizedPath, out bool isMissing)
    {
        try
        {
            var attributes = File.GetAttributes(normalizedPath);
            isMissing = (attributes & FileAttributes.Directory) != 0;
            return true;
        }
        catch (FileNotFoundException)
        {
            isMissing = true;
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            isMissing = true;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        isMissing = false;
        return false;
    }
}
