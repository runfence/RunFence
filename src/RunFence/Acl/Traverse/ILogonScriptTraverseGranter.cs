namespace RunFence.Acl.Traverse;

public interface ILogonScriptTraverseGranter
{
    List<string>? GrantTraverseAccess(string sid, string scriptsDirPath);

    void RevokeTraverseAccess(string sid, string scriptsDirPath);
}
