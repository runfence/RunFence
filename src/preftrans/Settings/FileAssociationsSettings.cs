namespace PrefTrans.Settings;

public class FileAssociation
{
    public string? ProgId { get; set; }
    public string? OpenCommand { get; set; }
}

public class FileAssociationsSettings
{
    public Dictionary<string, FileAssociation>? Associations { get; set; }
}