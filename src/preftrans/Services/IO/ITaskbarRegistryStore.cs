namespace PrefTrans.Services.IO;

public interface ITaskbarRegistryStore
{
    TaskbarExplorerAdvancedRegistryValues ReadExplorerAdvancedValues();

    bool WriteExplorerAdvancedValues(TaskbarExplorerAdvancedRegistryValues values);

    TaskbarTaskbandRegistryValues ReadTaskbandValues();

    bool WriteTaskbandValues(TaskbarTaskbandRegistryValues values);

    int? ReadSearchboxTaskbarMode();

    bool WriteSearchboxTaskbarMode(int value);
}
