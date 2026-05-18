using Moq;
using RunFence.Ipc;
using RunFence.Launch;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AssociationLaunchResolverTests
{
    [Fact]
    public void BuildRequest_UnquotedPathWithSpaces_PreservesRawArgumentAndNormalizedTarget()
    {
        var request = AssociationLaunchResolver.BuildRequest(".pdf", @"C:\Docs\My File.pdf");

        Assert.Equal(".pdf", request.AssociationKey);
        Assert.Equal(@"C:\Docs\My File.pdf", request.RawArgument);
        Assert.Equal(Path.GetFullPath(@"C:\Docs\My File.pdf"), request.NormalizedTarget);
    }

    [Fact]
    public void BuildRequest_QuotedTailWithExtraTokens_PreservesRawArgumentAndNormalizesFirstTarget()
    {
        var request = AssociationLaunchResolver.BuildRequest(".pdf", @"""C:\Docs\My File.pdf"" --page=2");

        Assert.Equal(@"""C:\Docs\My File.pdf"" --page=2", request.RawArgument);
        Assert.Equal(Path.GetFullPath(@"C:\Docs\My File.pdf"), request.NormalizedTarget);
    }

    [Fact]
    public void BuildRequest_UrlWithSpaces_PreservesRawArgumentAndNormalizedUrl()
    {
        var request = AssociationLaunchResolver.BuildRequest("http", @"https://example.com/a path/?x=1");

        Assert.Equal(@"https://example.com/a path/?x=1", request.RawArgument);
        Assert.Equal(@"https://example.com/a%20path/?x=1", request.NormalizedTarget);
    }

    [Fact]
    public void BuildRequest_QuotedRootedPathContainingAngleBracket_PreservesFileKind()
    {
        var request = AssociationLaunchResolver.BuildRequest(".pdf", @"""C:\Docs<\Bad.pdf"" --page=2");

        Assert.Equal(@"""C:\Docs<\Bad.pdf"" --page=2", request.RawArgument);
        Assert.Equal(Path.GetFullPath(@"C:\Docs<\Bad.pdf"), request.NormalizedTarget);
    }

    [Fact]
    public void Resolve_WithExplicitAuthorization_PrefersExplicitlyAuthorizedApp()
    {
        var database = new AppDatabase();
        var explicitApp = new AppEntry { Id = "explicit", AccountSid = "S-1-5-21-1", ExePath = @"C:\Apps\explicit.exe" };
        var fallbackApp = new AppEntry { Id = "fallback", AccountSid = "S-1-5-21-2", ExePath = @"C:\Apps\fallback.exe" };
        database.Apps.Add(explicitApp);
        database.Apps.Add(fallbackApp);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService
            .Setup(s => s.GetAllHandlerMappings(database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase)
            {
                [".pdf"] =
                [
                    new HandlerMappingEntry("fallback"),
                    new HandlerMappingEntry("explicit")
                ]
            });

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer
            .Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<AppEntry>(),
                database,
                It.IsAny<bool>()))
            .Returns(true);
        authorizer
            .Setup(a => a.HasExplicitPerAppAuthorization(It.IsAny<string?>(), explicitApp, database))
            .Returns(true);
        authorizer
            .Setup(a => a.HasExplicitPerAppAuthorization(It.IsAny<string?>(), fallbackApp, database))
            .Returns(false);

        var resolver = new AssociationLaunchResolver(() => handlerMappingService.Object, authorizer.Object);

        var result = resolver.Resolve(
            database,
            AssociationLaunchResolver.BuildRequest(".pdf", @"C:\Docs\My File.pdf"),
            callerIdentity: null,
            callerSid: "S-1-5-21-caller",
            identityFromImpersonation: true);

        Assert.Equal(AssociationLaunchResolutionStatus.Success, result.Status);
        Assert.Same(explicitApp, result.App);
        Assert.Equal("explicit", result.Entry?.AppId);
    }
}
