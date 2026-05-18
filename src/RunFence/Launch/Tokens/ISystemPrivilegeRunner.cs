namespace RunFence.Launch.Tokens;

public interface ISystemPrivilegeRunner
{
    void RunWithPrivileges(IEnumerable<string> privilegeNames, Action action);

    T RunWithPrivileges<T>(IEnumerable<string> privilegeNames, Func<T> action);
}
