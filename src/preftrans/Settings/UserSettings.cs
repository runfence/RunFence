namespace PrefTrans.Settings;

public class UserSettings
{
    public MouseSettings? Mouse { get; set; }
    public KeyboardSettings? Keyboard { get; set; }
    public ScrollSettings? Scroll { get; set; }
    public ExplorerSettings? Explorer { get; set; }
    public DesktopSettings? Desktop { get; set; }
    public TaskbarSettings? Taskbar { get; set; }
    public ThemeSettings? Theme { get; set; }
    public ScreenSaverSettings? ScreenSaver { get; set; }
    public InputLanguageSettings? InputLanguage { get; set; }
    public AccessibilitySettings? Accessibility { get; set; }
    public RegionalSettings? Regional { get; set; }
    public TrayIconsSettings? TrayIcons { get; set; }
    public NotificationSettings? Notifications { get; set; }
    public UserFoldersSettings? UserFolders { get; set; }
    public EnvironmentSettings? Environment { get; set; }
    public FileAssociationsSettings? FileAssociations { get; set; }
    public NightLightSettings? NightLight { get; set; }
}