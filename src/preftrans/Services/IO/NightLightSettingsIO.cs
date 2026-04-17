using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

/// <summary>
/// Reads and writes Night Light (blue light reduction) settings stored as opaque binary blobs
/// in the registry under HKCU\Software\Microsoft\Windows\CurrentVersion\CloudStore.
/// <para>
/// <b>Known limitation:</b> The binary blobs embed a CloudStore sequence number. When the blob
/// is written to a different account (or back to the same account after the sequence number has
/// advanced), Windows CloudStore may silently reject the write because the embedded sequence
/// number is stale. The settings will appear to be saved but may not take effect until the
/// sequence numbers are reconciled by the OS.
/// </para>
/// </summary>
public class NightLightSettingsIO(ISafeExecutor safe) : ISettingsIO
{
    public NightLightSettings Read()
    {
        var nightLight = new NightLightSettings();
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegNightLightState);
            nightLight.State = key?.GetValue("Data") as byte[];
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegNightLightSettings);
            nightLight.Settings = key?.GetValue("Data") as byte[];
        }, "reading");
        return nightLight;
    }

    public void Write(NightLightSettings nightLight)
    {
        safe.Try(() =>
        {
            if (nightLight.State != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegNightLightState);
                key.SetValue("Data", nightLight.State, RegistryValueKind.Binary);
            }
        }, "writing");
        safe.Try(() =>
        {
            if (nightLight.Settings != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegNightLightSettings);
                key.SetValue("Data", nightLight.Settings, RegistryValueKind.Binary);
            }
        }, "writing");
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.NightLight = Read();

    void ISettingsIO.WriteFrom(UserSettings s) { if (s.NightLight != null) Write(s.NightLight); }
}