using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class InputLanguageSettingsIO(ISafeExecutor safe, IBroadcastHelper broadcast) : ISettingsIO
{
    public InputLanguageSettings Read()
    {
        var inputLang = new InputLanguageSettings();
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegKeyboardLayout + @"\Preload");
            if (key != null)
            {
                inputLang.Preload = new Dictionary<string, string>();
                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is string v)
                        inputLang.Preload[name] = v;
                }
            }
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegKeyboardLayout + @"\Substitutes");
            if (key != null)
            {
                inputLang.Substitutes = new Dictionary<string, string>();
                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is string v)
                        inputLang.Substitutes[name] = v;
                }
            }
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegKeyboardLayout + @"\Toggle");
            if (key != null)
            {
                inputLang.SwitchHotkey = key.GetValue("Hotkey") as string;
                inputLang.LanguageHotkey = key.GetValue("Language Hotkey") as string;
                inputLang.LayoutHotkey = key.GetValue("Layout Hotkey") as string;
            }
        }, "reading");
        return inputLang;
    }

    public void Write(InputLanguageSettings inputLang)
    {
        bool changed = false;
        safe.Try(() =>
        {
            if (inputLang.Preload != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegKeyboardLayout + @"\Preload");
                // Delete all existing values first so stale entries from the target account
                // (e.g. previously configured input languages) do not persist alongside the
                // restored set, which could cause duplicate or conflicting language entries.
                foreach (var name in key.GetValueNames())
                    safe.Try(() => key.DeleteValue(name, throwOnMissingValue: false), "writing");
                foreach (var (name, val) in inputLang.Preload)
                {
                    key.SetValue(name, val, RegistryValueKind.String);
                    changed = true;
                }
            }
        }, "writing");
        safe.Try(() =>
        {
            if (inputLang.Substitutes != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegKeyboardLayout + @"\Substitutes");
                // Delete all existing values first — same reason as Preload.
                foreach (var name in key.GetValueNames())
                    safe.Try(() => key.DeleteValue(name, throwOnMissingValue: false), "writing");
                foreach (var (name, val) in inputLang.Substitutes)
                {
                    key.SetValue(name, val, RegistryValueKind.String);
                    changed = true;
                }
            }
        }, "writing");
        safe.Try(() =>
        {
            if (inputLang.SwitchHotkey != null || inputLang.LanguageHotkey != null || inputLang.LayoutHotkey != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegKeyboardLayout + @"\Toggle");
                if (inputLang.SwitchHotkey != null)
                    key.SetValue("Hotkey", inputLang.SwitchHotkey, RegistryValueKind.String);
                if (inputLang.LanguageHotkey != null)
                    key.SetValue("Language Hotkey", inputLang.LanguageHotkey, RegistryValueKind.String);
                if (inputLang.LayoutHotkey != null)
                    key.SetValue("Layout Hotkey", inputLang.LayoutHotkey, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        if (changed)
            broadcast.Broadcast();
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.InputLanguage = Read();

    void ISettingsIO.WriteFrom(UserSettings s) { if (s.InputLanguage != null) Write(s.InputLanguage); }
}