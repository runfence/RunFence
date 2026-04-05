namespace PrefTrans.Settings;

public class InputLanguageSettings
{
    public Dictionary<string, string>? Preload { get; set; }
    public Dictionary<string, string>? Substitutes { get; set; }
    public string? SwitchHotkey { get; set; }
    public string? LanguageHotkey { get; set; }
    public string? LayoutHotkey { get; set; }
}