namespace PrefTrans.Settings;

public class NotificationSettings
{
    public int? GlobalToastsEnabled { get; set; }
    public Dictionary<string, bool>? PerAppSuppression { get; set; }
}