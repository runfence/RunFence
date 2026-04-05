using System.Diagnostics;
using System.Security;

namespace RunFence.Launch;

public static class ProcessStartInfoHelper
{
    public static void SetCredentials(ProcessStartInfo psi, string? username, string? domain, SecureString? password)
    {
        psi.UserName = username;
        psi.Domain = string.IsNullOrEmpty(domain) ? null : domain;
        psi.Password = password;
        psi.LoadUserProfile = true;
    }
}