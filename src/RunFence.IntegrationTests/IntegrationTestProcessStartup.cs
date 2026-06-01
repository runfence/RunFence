using System.Runtime.CompilerServices;
using RunFence.Launch.Tokens;

namespace RunFence.IntegrationTests;

internal static class IntegrationTestProcessStartup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        AppContext.SetSwitch("RunFence.UnitTests.UseAdminOperationMocks", true);

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
