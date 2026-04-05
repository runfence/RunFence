namespace RunFence.Account;

public interface IAccountLoginRestrictionService
{
    bool IsAccountHidden(string username);
    void SetAccountHidden(string username, string sid, bool hidden);
    bool IsLoginBlockedBySid(string sid);
    SetLoginBlockedResult SetLoginBlockedBySid(string sid, string username, bool blocked);

    /// <summary>Returns true=both conditions (logon script + hidden), false=neither, null=partial.</summary>
    bool? GetNoLogonState(string sid, string? username);

    /// <summary>
    /// Controls whether administrator accounts are enumerated in UAC elevation prompts.
    /// When <paramref name="enumerate"/> is true, restores the default Windows behavior (deletes the registry value).
    /// When false, sets <c>EnumerateAdministrators=0</c> in the CredUI policy key to suppress enumeration.
    /// </summary>
    void SetUacAdminEnumeration(bool enumerate);
}