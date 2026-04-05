namespace RunFence.UI;

/// <summary>
/// Display item representing a config file path in the config combo box.
/// </summary>
public class ConfigComboItem(string? path)
{
    public string? Path { get; } = path;

    public override string ToString() => Path == null ? "Main Config" : System.IO.Path.GetFileName(Path);
}