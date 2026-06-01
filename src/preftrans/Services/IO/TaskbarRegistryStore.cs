using Microsoft.Win32;
using PrefTrans.Native;

namespace PrefTrans.Services.IO;

public class TaskbarRegistryStore(
    ISafeExecutor safe)
    : ITaskbarRegistryStore
{
    public TaskbarExplorerAdvancedRegistryValues ReadExplorerAdvancedValues()
    {
        var values = new TaskbarExplorerAdvancedRegistryValues();
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegExplorerAdvanced);
            if (key == null)
                return;
            values = values with
            {
                TaskbarSmallIcons = key.GetValue("TaskbarSmallIcons") as int?,
                ShowTaskViewButton = key.GetValue("ShowTaskViewButton") as int?,
                TaskbarAlignment = key.GetValue("TaskbarAl") as int?,
                ShowWidgets = key.GetValue("TaskbarDa") as int?,
                ButtonCombine = key.GetValue("TaskbarGlomLevel") as int?,
                MultiMonitorButtonCombine = key.GetValue("MMTaskbarGlomLevel") as int?,
                VirtualDesktopTaskbarFilter = key.GetValue("VirtualDesktopTaskbarFilter") as int?
            };
        }, "reading");
        return values;
    }

    public bool WriteExplorerAdvancedValues(TaskbarExplorerAdvancedRegistryValues values)
    {
        bool changed = false;
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegExplorerAdvanced);

            void Set(string name, int? value)
            {
                if (!value.HasValue)
                    return;

                if (key.GetValue(name) as int? == value.Value)
                    return;

                key.SetValue(name, value.Value, RegistryValueKind.DWord);
                changed = true;
            }

            Set("TaskbarSmallIcons", values.TaskbarSmallIcons);
            Set("ShowTaskViewButton", values.ShowTaskViewButton);
            Set("TaskbarAl", values.TaskbarAlignment);
            Set("TaskbarDa", values.ShowWidgets);
            Set("TaskbarGlomLevel", values.ButtonCombine);
            Set("MMTaskbarGlomLevel", values.MultiMonitorButtonCombine);
            Set("VirtualDesktopTaskbarFilter", values.VirtualDesktopTaskbarFilter);
        }, "writing");
        return changed;
    }

    public TaskbarTaskbandRegistryValues ReadTaskbandValues()
    {
        var values = new TaskbarTaskbandRegistryValues();
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegTaskband);
            if (key == null)
                return;
            values = values with
            {
                Favorites = key.GetValue("Favorites") as byte[],
                FavoritesResolve = key.GetValue("FavoritesResolve") as byte[]
            };
        }, "reading");
        return values;
    }

    public bool WriteTaskbandValues(TaskbarTaskbandRegistryValues values)
    {
        bool changed = false;
        safe.Try(() =>
        {
            if (values.Favorites == null && values.FavoritesResolve == null)
                return;

            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegTaskband);
            if (values.Favorites != null && !ByteArrayEquals(key.GetValue("Favorites") as byte[], values.Favorites))
            {
                key.SetValue("Favorites", values.Favorites, RegistryValueKind.Binary);
                changed = true;
            }

            if (values.FavoritesResolve != null &&
                !ByteArrayEquals(key.GetValue("FavoritesResolve") as byte[], values.FavoritesResolve))
            {
                key.SetValue("FavoritesResolve", values.FavoritesResolve, RegistryValueKind.Binary);
                changed = true;
            }
        }, "writing");
        return changed;
    }

    public int? ReadSearchboxTaskbarMode()
    {
        int? result = null;
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegSearch);
            result = key?.GetValue("SearchboxTaskbarMode") as int?;
        }, "reading");
        return result;
    }

    public bool WriteSearchboxTaskbarMode(int value)
    {
        bool changed = false;
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegSearch);
            if (key.GetValue("SearchboxTaskbarMode") as int? == value)
                return;

            key.SetValue("SearchboxTaskbarMode", value, RegistryValueKind.DWord);
            changed = true;
        }, "writing");
        return changed;
    }

    private static bool ByteArrayEquals(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Length != right.Length)
            return false;

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }
}
