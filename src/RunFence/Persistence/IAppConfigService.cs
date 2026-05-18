using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IAppConfigService
{
    // --- Query ---
    string? GetConfigPath(string appId);
    List<AppEntry> GetAppsForConfig(string path, AppDatabase database);
    AppConfig GetConfigForExport(string? path, AppDatabase database);
    IReadOnlyList<string> GetLoadedConfigPaths();
    bool HasLoadedConfigs { get; }
    AppConfigRuntimeStateSnapshot CaptureRuntimeStateSnapshot();
    void RestoreRuntimeStateSnapshot(AppConfigRuntimeStateSnapshot snapshot);

    // --- Load / Unload ---
    AdditionalConfigLoadData ReadAdditionalConfig(string path, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey);
    AdditionalConfigLoadData ReadAdditionalConfigFromBackup(string configPath, AppConfig backupConfig, AppDatabase database);
    List<AppEntry> ApplyAdditionalConfig(AdditionalConfigLoadData configData, AppDatabase database);
    List<AppEntry> LoadAdditionalConfig(string path, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey);
    List<AppEntry> UnloadConfig(string path, AppDatabase database);
    void CreateEmptyConfig(string path, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt);

    // --- Mapping ---
    void AssignApp(string appId, string? configPath);
    void RemoveApp(string appId);

    // --- Save ---
    void SaveConfigForApp(string appId, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt);
    void SaveConfigAtPath(string configPath, AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt);
    void SaveAllConfigs(AppDatabase database, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt);
    void ReencryptAndSaveAll(CredentialStore store, AppDatabase database, ISecureSecretSnapshotSource newPinDerivedKey);
    void SaveImportedConfig(string path, AppConfig config, ISecureSecretSnapshotSource pinDerivedKey, byte[] argonSalt);
}
