namespace PrefTrans.Settings;

public class ScreenSaverSettings
{
    public bool? Enabled { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? ExecutablePath { get; set; }
    public string? RequirePassword { get; set; }
    public int? DelayLockInterval { get; set; }
}