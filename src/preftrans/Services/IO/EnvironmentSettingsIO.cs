using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public static class EnvironmentSettingsIO
{
    public static EnvironmentSettings Read()
    {
        var env = new EnvironmentSettings();
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegEnvironment);
            if (key == null)
                return;
            env.Variables = new Dictionary<string, EnvVar>();
            foreach (var name in key.GetValueNames())
            {
                if (Constants.BlockedEnvVars.Contains(name))
                    continue;
                if (Constants.BlockedEnvVarsParts.Any(x => name.Contains(x, StringComparison.InvariantCultureIgnoreCase)))
                    continue;
                var kind = key.GetValueKind(name);
                if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                    continue;
                if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string val)
                    continue;
                env.Variables[name] = new EnvVar
                {
                    Value = val,
                    Kind = kind == RegistryValueKind.ExpandString ? "ExpandSZ" : "SZ",
                };
            }
        }, "reading");
        return env;
    }

    public static void Write(EnvironmentSettings env)
    {
        if (env.Variables == null)
            return;
        bool changed = false;
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegEnvironment);
            foreach (var (name, envVar) in env.Variables)
            {
                if (envVar.Value == null)
                    continue;
                var kind = envVar.Kind == "ExpandSZ" ? RegistryValueKind.ExpandString : RegistryValueKind.String;
                key.SetValue(name, envVar.Value, kind);
                changed = true;
            }
        }, "writing");
        if (changed)
            BroadcastHelper.BroadcastEnvironment();
    }
}