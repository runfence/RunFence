namespace RunFence.Apps.Shortcuts;

public sealed class ShortcutComGateway(
    IShortcutComHelper shortcutComHelper,
    IShortcutFilePersistenceNative shortcutFilePersistenceNative) : IShortcutGateway
{
    public ShortcutData Read(string shortcutPath)
        => shortcutComHelper.WithShortcut(shortcutPath, shortcut =>
        {
            var iconLocation = (string?)shortcut.IconLocation;
            ParseIconLocation(iconLocation, out var iconPath, out var iconIndex);
            return new ShortcutData(
                (string?)shortcut.TargetPath ?? string.Empty,
                (string?)shortcut.Arguments,
                (string?)shortcut.WorkingDirectory,
                iconPath,
                iconIndex,
                (string?)shortcut.Description,
                ParseHotkey((string?)shortcut.Hotkey),
                (int?)shortcut.WindowStyle ?? 1);
        });

    public ShortcutMutation ReadMutationState(string shortcutPath)
        => shortcutComHelper.WithShortcut(shortcutPath, shortcut =>
        {
            return new ShortcutMutation(
                (string?)shortcut.TargetPath ?? string.Empty,
                (string?)shortcut.Arguments,
                (string?)shortcut.WorkingDirectory,
                (string?)shortcut.IconLocation,
                ShortcutIconUpdateMode.None,
                (string?)shortcut.Description,
                SerializeHotkey(shortcut.Hotkey),
                (int?)shortcut.WindowStyle ?? 1);
        });

    public void Write(string shortcutPath, ShortcutData data)
    {
        shortcutComHelper.WithShortcut(shortcutPath, shortcut =>
        {
            shortcut.TargetPath = data.TargetPath;
            shortcut.Arguments = data.Arguments;
            shortcut.WorkingDirectory = data.WorkingDirectory;
            shortcut.Description = data.Description;
            shortcut.Hotkey = data.Hotkey;
            shortcut.WindowStyle = data.WindowStyle;
            shortcut.IconLocation = string.IsNullOrWhiteSpace(data.IconPath)
                ? string.Empty
                : $"{data.IconPath},{data.IconIndex}";
            shortcut.Save();
        });
    }

    public void WriteMutationState(string shortcutPath, ShortcutMutation mutation)
    {
        shortcutComHelper.WithShortcut(shortcutPath, shortcut =>
        {
            shortcut.TargetPath = mutation.TargetPath;
            shortcut.Arguments = mutation.Arguments;
            shortcut.WorkingDirectory = mutation.WorkingDirectory;
            shortcut.Description = mutation.Description;
            shortcut.Hotkey = mutation.Hotkey;
            shortcut.WindowStyle = mutation.WindowStyle;

            if (mutation.IconUpdateMode == ShortcutIconUpdateMode.Set)
            {
                shortcut.IconLocation = mutation.IconLocation;
            }
            else if (mutation.IconUpdateMode == ShortcutIconUpdateMode.ClearBestEffort)
            {
                try
                {
                    shortcut.IconLocation = "";
                }
                catch
                {
                }
            }
            else if (!string.IsNullOrWhiteSpace(mutation.IconLocation))
            {
                shortcut.IconLocation = mutation.IconLocation;
            }

            shortcut.Save();
        });
    }

    public void Delete(string shortcutPath)
        => shortcutFilePersistenceNative.DeleteExistingDestination(shortcutPath);

    private static short ParseHotkey(object? hotkey)
    {
        return hotkey switch
        {
            short shortValue => shortValue,
            int intValue when intValue is >= short.MinValue and <= short.MaxValue => (short)intValue,
            string stringValue when short.TryParse(stringValue, out var parsed) => parsed,
            _ => 0
        };
    }

    private static string? SerializeHotkey(object? hotkey)
    {
        return hotkey switch
        {
            null => null,
            short shortValue => shortValue.ToString(),
            int intValue => intValue.ToString(),
            string stringValue => stringValue,
            _ => hotkey.ToString()
        };
    }

    private static void ParseIconLocation(string? iconLocation, out string? iconPath, out int iconIndex)
    {
        iconPath = null;
        iconIndex = 0;
        if (string.IsNullOrWhiteSpace(iconLocation))
            return;

        var lastComma = iconLocation.LastIndexOf(',');
        if (lastComma <= 0 || lastComma >= iconLocation.Length - 1)
        {
            iconPath = iconLocation;
            return;
        }

        if (!int.TryParse(iconLocation[(lastComma + 1)..], out iconIndex))
        {
            iconPath = iconLocation;
            iconIndex = 0;
            return;
        }

        iconPath = iconLocation[..lastComma];
    }
}
