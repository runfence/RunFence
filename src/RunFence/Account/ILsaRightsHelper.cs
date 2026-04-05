namespace RunFence.Account;

public interface ILsaRightsHelper
{
    byte[] GetSidBytes(string sidString);
    byte[]? TryResolveSidBytes(string? domain, string username);
    List<string> EnumerateAccountRights(byte[] sidBytes);
    void AddAccountRights(byte[] sidBytes, string[] rights);
    void RemoveAccountRights(byte[] sidBytes, string[] rights);
}