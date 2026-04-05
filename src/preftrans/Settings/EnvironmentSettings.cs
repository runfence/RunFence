namespace PrefTrans.Settings;

public class EnvVar
{
    public string? Value { get; set; }
    public string? Kind { get; set; }
}

public class EnvironmentSettings
{
    public Dictionary<string, EnvVar>? Variables { get; set; }
}