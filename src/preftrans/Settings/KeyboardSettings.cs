namespace PrefTrans.Settings;

public class KeyboardSettings
{
    public int? KeyboardDelay { get; set; }
    public int? KeyboardSpeed { get; set; }
    public string? NumLockOnStartup { get; set; } // InitialKeyboardIndicators: "0"=off, "2"=on
}