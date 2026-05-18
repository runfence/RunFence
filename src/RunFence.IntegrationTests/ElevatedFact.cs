using System.Security.Principal;
using Xunit;

namespace RunFence.IntegrationTests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ElevatedFact : FactAttribute
{
    public ElevatedFact()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            Skip = "RunFence integration tests must be run from an elevated administrator shell.";
    }
}
