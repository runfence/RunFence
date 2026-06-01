using Moq;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class ContextMenuServiceTests : IDisposable
{
    private readonly TempDirectory _tempDir;
    private readonly InMemoryRegistryKey _hklmRoot;
    private readonly string _launcherPath;

    public ContextMenuServiceTests()
    {
        _hklmRoot = InMemoryRegistryKey.CreateRoot("ContextMenu");
        _tempDir = new TempDirectory("RunFence_ContextMenuTest");
        _launcherPath = Path.Combine(_tempDir.Path, PathConstants.LauncherExeName);
        File.WriteAllBytes(_launcherPath, [0x4D, 0x5A]);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
        _hklmRoot.Dispose();
    }

    [Fact]
    public void Register_ExportsManagedProgramDataIconAndWritesRegistryIconPath()
    {
        var log = new Mock<ILoggingService>();
        var iconProvider = new Mock<IAppIconProvider>(MockBehavior.Strict);
        var objectProvisioner = new Mock<IProgramDataObjectProvisioner>(MockBehavior.Strict);
        var pathResolver = new Mock<IProgramDataKnownPathResolver>(MockBehavior.Strict);
        var service = new ContextMenuService(
            log.Object,
            iconProvider.Object,
            objectProvisioner.Object,
            pathResolver.Object,
            _hklmRoot,
            _launcherPath);
        var exportedIconFile = Path.Combine(_tempDir.Path, "RunFence.ico");

        iconProvider.Setup(provider => provider.GetAppIcon()).Returns(SystemIcons.Application);
        pathResolver
            .Setup(resolver => resolver.GetFilePath(ProgramDataPolicies.ContextMenuIcon))
            .Returns(exportedIconFile);
        objectProvisioner
            .Setup(service => service.CreateOrReplaceFile(
                ProgramDataPolicies.ContextMenuIcon,
                FileShare.Read))
            .Returns(() => new FileStream(exportedIconFile, FileMode.Create, FileAccess.ReadWrite, FileShare.Read));

        service.Register();

        Assert.True(new FileInfo(exportedIconFile).Length > 0);
        foreach (var registryPath in PathConstants.ContextMenuRegistryPaths)
        {
            using var shellKey = _hklmRoot.OpenSubKey(registryPath);
            Assert.NotNull(shellKey);
            Assert.Equal(exportedIconFile, shellKey.GetValue("Icon") as string);
        }

        objectProvisioner.VerifyAll();
        pathResolver.VerifyAll();
    }
}
