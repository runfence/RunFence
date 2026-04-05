using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IAppConfigService
{
    // --- Query ---
    string? GetConfigPath(string appId);
    List<AppEntry> GetAppsForConfig(string path, AppDatabase database);
    IReadOnlyList<string> GetLoadedConfigPaths();
    bool HasLoadedConfigs { get; }
    List<string> GetUnavailableConfigPaths();

    // --- Load / Unload ---
    List<AppEntry> LoadAdditionalConfig(string path, AppDatabase database, byte[] pinDerivedKey);
    List<AppEntry> UnloadConfig(string path, AppDatabase database);
    void CreateEmptyConfig(string path, byte[] pinDerivedKey, byte[] argonSalt);

    // --- Mapping ---
    void AssignApp(string appId, string? configPath);
    void RemoveApp(string appId);

    // --- Save ---
    void SaveConfigForApp(string appId, AppDatabase database, byte[] pinDerivedKey, byte[] argonSalt);
    void SaveConfigAtPath(string configPath, AppDatabase database, byte[] pinDerivedKey, byte[] argonSalt);
    void SaveAllConfigs(AppDatabase database, byte[] pinDerivedKey, byte[] argonSalt);
    void ReencryptAndSaveAll(CredentialStore store, AppDatabase database, byte[] newPinDerivedKey);
    void SaveImportedConfig(string path, List<AppEntry> apps, byte[] pinDerivedKey, byte[] argonSalt);
}