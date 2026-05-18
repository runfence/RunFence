using System.Runtime.CompilerServices;
using RunFence.Launch.Tokens;

#pragma warning disable CS0162 // Unreachable code detected

namespace RunFence.IntegrationTests;

internal static class IntegrationTestProcessStartup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
#if !DEBUG
        throw new Exception("The integration test suite is not allowed to run in Release Configuration to prevent damaging real user configs");
#endif
        foreach (var privilege in new[]
                 {
                     TokenPrivilegeHelper.SeBackupPrivilege,
                     TokenPrivilegeHelper.SeRestorePrivilege,
                     TokenPrivilegeHelper.SeTakeOwnershipPrivilege,
                     TokenPrivilegeHelper.SeImpersonatePrivilege,
                     TokenPrivilegeHelper.SeIncreaseQuotaPrivilege,
                     TokenPrivilegeHelper.SeDebugPrivilege,
                     TokenPrivilegeHelper.SeRelabelPrivilege
                 })
        {
            try
            {
                TokenPrivilegeHelper.EnablePrivileges([privilege]);
            }
            catch
            {
            }
        }
    }
}
