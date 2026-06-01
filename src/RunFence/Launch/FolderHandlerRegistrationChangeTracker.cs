using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Launch;

public sealed class FolderHandlerRegistrationChangeTracker
{
    private IRegistryKey? _usersRoot;
    private string? _accountSid;
    private readonly List<FolderHandlerRegistryValueSnapshot> _valueSnapshots = [];
    private readonly HashSet<string> _snapshotKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _createdKeyPaths = [];
    private readonly HashSet<string> _createdKeySet = new(StringComparer.OrdinalIgnoreCase);

    public bool RegistryChanged { get; private set; }
    public bool RunOnceChanged { get; private set; }

    public FolderHandlerRegistrationChangeTracker Initialize(IRegistryKey usersRoot, string accountSid)
    {
        _usersRoot = usersRoot;
        _accountSid = accountSid;
        _valueSnapshots.Clear();
        _snapshotKeys.Clear();
        _createdKeyPaths.Clear();
        _createdKeySet.Clear();
        RegistryChanged = false;
        RunOnceChanged = false;
        return this;
    }

    public void EnsureKey(string subKeyPath)
    {
        using var _ = EnsureKeyExists(subKeyPath);
    }

    public IRegistryKey OpenWritableClassesKey(string subKeyPath)
    {
        var fullPath = FolderHandlerRegistryPathMapper.BuildFullPath(AccountSid, subKeyPath);
        return UsersRoot.OpenSubKey(fullPath, writable: true)
               ?? throw new InvalidOperationException($"Failed to open registry key: {fullPath}");
    }

    public void SetValue(
        string subKeyPath,
        string? valueName,
        object value,
        RegistryValueKind valueKind,
        bool isRunOnceValue)
    {
        using var key = EnsureKeyExists(subKeyPath);
        var normalizedValueName = FolderHandlerRegistryPathMapper.NormalizeValueName(valueName);
        var existingValue = key.GetValue(normalizedValueName);
        if (ValuesEqual(existingValue, value) && ValueExists(key, normalizedValueName) && key.GetValueKind(normalizedValueName) == valueKind)
            return;

        CaptureValueSnapshot(key, subKeyPath, normalizedValueName);
        key.SetValue(normalizedValueName, value, valueKind);
        key.Flush();
        MarkChanged(isRunOnceValue);
    }

    public void DeleteValue(string subKeyPath, string valueName, bool isRunOnceValue)
    {
        using var key = UsersRoot.OpenSubKey(FolderHandlerRegistryPathMapper.BuildFullPath(AccountSid, subKeyPath), writable: true);
        if (key == null || !ValueExists(key, valueName))
            return;

        CaptureValueSnapshot(key, subKeyPath, valueName);
        key.DeleteValue(valueName, throwOnMissingValue: false);
        key.Flush();
        MarkChanged(isRunOnceValue);
    }

    public FolderHandlerRegistrationMaintenanceResult BuildResult(bool hadOwnedRegistrationBeforeCall)
    {
        return new FolderHandlerRegistrationMaintenanceResult
        {
            RegistryChanged = RegistryChanged,
            RunOnceChanged = RunOnceChanged,
            HadOwnedRegistrationBeforeCall = hadOwnedRegistrationBeforeCall,
            ChangeSet = _valueSnapshots.Count == 0 && _createdKeyPaths.Count == 0
                ? null
                : new FolderHandlerRegistrationChangeSet
                {
                    ValueSnapshots = _valueSnapshots.ToList(),
                    CreatedKeyPaths = _createdKeyPaths.ToList()
                }
        };
    }

    private IRegistryKey UsersRoot => _usersRoot ?? throw new InvalidOperationException("FolderHandlerRegistrationChangeTracker.Initialize must be called first.");
    private string AccountSid => _accountSid ?? throw new InvalidOperationException("FolderHandlerRegistrationChangeTracker.Initialize must be called first.");

    private IRegistryKey EnsureKeyExists(string subKeyPath)
    {
        var fullPath = FolderHandlerRegistryPathMapper.BuildFullPath(AccountSid, subKeyPath);
        CaptureCreatedKeyChain(subKeyPath);
        return UsersRoot.CreateSubKey(fullPath)
               ?? throw new InvalidOperationException($"Failed to create registry key: {fullPath}");
    }

    private void CaptureCreatedKeyChain(string subKeyPath)
    {
        var segments = subKeyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        foreach (var segment in segments)
        {
            current = string.IsNullOrEmpty(current) ? segment : $@"{current}\{segment}";
            if (_createdKeySet.Contains(current))
                continue;

            using var existingKey = UsersRoot.OpenSubKey(FolderHandlerRegistryPathMapper.BuildFullPath(AccountSid, current));
            if (existingKey != null)
                continue;

            _createdKeySet.Add(current);
            _createdKeyPaths.Add(current);
        }
    }

    private void CaptureValueSnapshot(IRegistryKey key, string subKeyPath, string? valueName)
    {
        var snapshotKey = FolderHandlerRegistryPathMapper.BuildValueIdentifier(subKeyPath, valueName);
        if (!_snapshotKeys.Add(snapshotKey))
            return;

        var normalizedValueName = FolderHandlerRegistryPathMapper.NormalizeValueName(valueName);
        var existed = ValueExists(key, normalizedValueName);
        _valueSnapshots.Add(new FolderHandlerRegistryValueSnapshot
        {
            SubKeyPath = subKeyPath,
            ValueName = valueName,
            Existed = existed,
            PreviousValue = existed ? key.GetValue(normalizedValueName) : null,
            PreviousKind = existed ? key.GetValueKind(normalizedValueName) : null
        });
    }

    private void MarkChanged(bool isRunOnceValue)
    {
        if (isRunOnceValue)
            RunOnceChanged = true;
        else
            RegistryChanged = true;
    }

    private static bool ValueExists(IRegistryKey key, string? valueName)
        => Array.Exists(
            key.GetValueNames(),
            existingName => string.Equals(
                existingName,
                FolderHandlerRegistryPathMapper.NormalizeValueName(valueName),
                StringComparison.OrdinalIgnoreCase));

    private static bool ValuesEqual(object? left, object? right)
        => string.Equals(left as string, right as string, StringComparison.Ordinal);
}
