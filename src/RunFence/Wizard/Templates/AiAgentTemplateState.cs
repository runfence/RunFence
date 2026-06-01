namespace RunFence.Wizard.Templates;

public sealed class AiAgentTemplateState
{
    public string Username { get; set; } = string.Empty;
    public bool UseAiPackage { get; set; } = true;
    public List<string> ProjectPaths { get; set; } = [];
    public string? AppPath { get; set; }
    public string? CreatedSid { get; set; }
    public bool AllowInternet { get; set; }
    public bool AllowLan { get; set; }
    public bool AllowLocalhost { get; set; }

    public void Reset()
    {
        Username = string.Empty;
        UseAiPackage = true;
        ProjectPaths = [];
        AppPath = null;
        CreatedSid = null;
        AllowInternet = false;
        AllowLan = false;
        AllowLocalhost = false;
    }
}
