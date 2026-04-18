using Moq;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class ConfigImportHandlerTests
{
    [Fact]
    public void ImportMainConfig_ImportsAppContainers_SkipsDuplicates()
    {
        // Arrange
        var appConfigService = new Mock<IAppConfigService>();
        var licenseService = new Mock<ILicenseService>();
        var sessionProvider = new Mock<ISessionProvider>();
        var pathGrantService = new Mock<IPathGrantService>();
        var grantTracker = new Mock<IGrantConfigTracker>();
        var log = new Mock<ILoggingService>();
        var idGenerator = new Mock<IAppEntryIdGenerator>();

        licenseService.Setup(l => l.IsLicensed).Returns(true);
        appConfigService.Setup(s => s.HasLoadedConfigs).Returns(false);
        appConfigService.Setup(s => s.GetConfigPath(It.IsAny<string>())).Returns((string?)null);
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns(Array.Empty<string>());
        idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>()))
            .Returns(() => Guid.NewGuid().ToString("N"));

        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "existing-container", DisplayName = "Existing" });

        var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore(),
            PinDerivedKey = pinKey
        };
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        var handler = new ConfigImportHandler(
            appConfigService.Object, licenseService.Object, sessionProvider.Object,
            pathGrantService.Object, grantTracker.Object, log.Object, idGenerator.Object);

        var importedDb = new AppDatabase();
        importedDb.AppContainers.Add(new AppContainerEntry { Name = "existing-container", DisplayName = "Duplicate" });
        importedDb.AppContainers.Add(new AppContainerEntry { Name = "new-container", DisplayName = "New" });

        var json = System.Text.Json.JsonSerializer.Serialize(importedDb, JsonDefaults.Options);
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);

            // Act
            handler.ImportMainConfig(tempFile);

            // Assert
            Assert.Equal(2, database.AppContainers.Count);
            Assert.Contains(database.AppContainers, c =>
                string.Equals(c.Name, "existing-container", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(database.AppContainers, c =>
                string.Equals(c.Name, "new-container", StringComparison.OrdinalIgnoreCase));
            var existing = database.AppContainers.First(c =>
                string.Equals(c.Name, "existing-container", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Existing", existing.DisplayName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
