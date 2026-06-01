using Moq;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AppEntryAclEnforcerTests
{
    private readonly Mock<IAclService> _aclService = new();
    private readonly AppEntryAclEnforcer _enforcer;

    public AppEntryAclEnforcerTests()
    {
        _enforcer = new AppEntryAclEnforcer(_aclService.Object);
    }

    [Fact]
    public void Revert_AppInAllApps_RevertAclReceivesListContainingApp()
    {
        var app = new AppEntry { Id = "cnt01", Name = "ContainerApp", RestrictAcl = true, ExePath = @"C:\test.exe" };
        var otherApp = new AppEntry { Id = "oth01", Name = "OtherApp" };
        var allApps = new List<AppEntry> { app, otherApp };

        IReadOnlyList<AppEntry>? capturedAllApps = null;
        _aclService
            .Setup(a => a.RevertAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<AppEntry, IReadOnlyList<AppEntry>>((_, apps) => capturedAllApps = apps);

        _enforcer.Revert(app, allApps);

        Assert.NotNull(capturedAllApps);
        Assert.Contains(app, capturedAllApps);
        _aclService.Verify(a => a.RevertAcl(app, allApps), Times.Once);
    }

[Fact]
    public void Apply_RestrictAclTrue_CallsApplyAcl()
    {
        var app = new AppEntry { Name = "App", RestrictAcl = true, ExePath = @"C:\app.exe" };
        var allApps = new List<AppEntry> { app };

        _enforcer.Apply(app, allApps);

        _aclService.Verify(a => a.ApplyAcl(app, allApps), Times.Once);
    }

    [Fact]
    public void Apply_UrlSchemeApp_SkipsAcl()
    {
        var app = new AppEntry { Name = "UrlApp", IsUrlScheme = true, RestrictAcl = true };

        _enforcer.Apply(app, new List<AppEntry> { app });

        _aclService.Verify(a => a.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }
}
