using RunFence.Core;

namespace RunFence.Wizard.Templates;

public sealed class GamingAccountTemplateState
{
    public bool IsExistingAccount { get; set; }
    public string? ExistingAccountSid { get; set; }
    public ProtectedString? CollectedPassword { get; set; }
    public string Username { get; set; } = string.Empty;
    public ProtectedString? Password { get; set; }
    public List<string> GameFolders { get; set; } = [];
    public List<string> GameLaunchers { get; set; } = [];

    public void Reset()
    {
        IsExistingAccount = false;
        ExistingAccountSid = null;
        CollectedPassword?.Dispose();
        CollectedPassword = null;
        Username = string.Empty;
        Password?.Dispose();
        Password = null;
        GameFolders = [];
        GameLaunchers = [];
    }

    public void DisposeSecrets()
    {
        CollectedPassword?.Dispose();
        CollectedPassword = null;
        Password?.Dispose();
        Password = null;
    }
}
