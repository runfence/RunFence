using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class EnvironmentSettingsIO(ISafeExecutor safe, IBroadcastHelper broadcast) : ISettingsIO
{
    public EnvironmentSettings Read()
    {
        var env = new EnvironmentSettings();
        safe.Try(() =>
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

    /// <remarks>
    /// Write semantics are <b>additive (merge)</b>: existing variables in the target account that
    /// are not present in <paramref name="env"/> are left untouched. This is intentional — the
    /// target account may have custom variables (e.g., tool-specific PATH additions) that should
    /// not be removed. Only variables explicitly present in the imported settings are written.
    /// <para>
    /// Write path intentionally uses exact-match BlockedEnvVars only — partial-match
    /// BlockedEnvVarsParts filter is for read-path display safety, not a security boundary.
    /// preftrans runs elevated with full env access.
    /// </para>
    /// </remarks>
    public void Write(EnvironmentSettings env)
    {
        if (env.Variables == null)
            return;
        bool changed = false;
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegEnvironment);
            foreach (var (name, envVar) in env.Variables)
            {
                if (envVar.Value == null)
                    continue;
                if (Constants.BlockedEnvVars.Contains(name))
                    continue;
                // Sensitive substring filtering intentionally not applied to Write — preftrans operates under target user credentials.
                var kind = envVar.Kind == "ExpandSZ" ? RegistryValueKind.ExpandString : RegistryValueKind.String;
                key.SetValue(name, envVar.Value, kind);
                changed = true;
            }
        }, "writing");
        if (changed)
            broadcast.BroadcastEnvironment();
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.Environment = Read();
    void ISettingsIO.WriteFrom(UserSettings s) { if (s.Environment != null) Write(s.Environment); }
}
