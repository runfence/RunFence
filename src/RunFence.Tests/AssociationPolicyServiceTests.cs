using Moq;
using RunFence.Apps;
using RunFence.Core.Models;
using RunFence.Ipc;
using Xunit;

namespace RunFence.Tests;

public class AssociationPolicyServiceTests
{
    [Fact]
    public void ResolveConflictsForSid_ExplicitPerAppAuth_AppMappingWins()
    {
        var authorizer = new Mock<IIpcCallerAuthorizer>();
        var service = new AssociationPolicyService(authorizer.Object);
        var db = new AppDatabase();
        var app = new AppEntry { Id = "app1", Name = "App 1" };
        db.Apps.Add(app);

        var appMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new("app1")
        };
        var directMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new() { ClassName = "txtfile" }
        };

        authorizer.Setup(a => a.HasExplicitPerAppAuthorization("S-1", app, db)).Returns(true);

        service.ResolveConflictsForSid("S-1", appMappings, directMappings, db);

        Assert.True(appMappings.ContainsKey(".txt"));
        Assert.False(directMappings.ContainsKey(".txt"));
    }
}
