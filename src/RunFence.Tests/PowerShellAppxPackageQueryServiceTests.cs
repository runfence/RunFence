using Moq;
using RunFence.Apps.Shortcuts;
using RunFence.Infrastructure;
using System.Text;
using Xunit;

namespace RunFence.Tests;

public sealed class PowerShellAppxPackageQueryServiceTests
{
    [Fact]
    public void QueryPackages_ParsesArrayOutput()
    {
        var processExecution = CreateProcessExecutionService("""
            [{"PackageFamilyName":"Contoso.App_8wekyb3d8bbwe","PackageFullName":"Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe","InstallLocation":"C:\\Program Files\\WindowsApps\\Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe"}]
            """);
        var service = new PowerShellAppxPackageQueryService(processExecution.Object);

        var packages = service.QueryPackages();

        var package = Assert.Single(packages);
        Assert.Equal("Contoso.App_8wekyb3d8bbwe", package.PackageFamilyName);
    }

    [Fact]
    public void QueryPackages_ParsesSingleObjectOutput()
    {
        var processExecution = CreateProcessExecutionService("""
            {"PackageFamilyName":"Contoso.App_8wekyb3d8bbwe","PackageFullName":"Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe","InstallLocation":"C:\\Program Files\\WindowsApps\\Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe"}
            """);
        var service = new PowerShellAppxPackageQueryService(processExecution.Object);

        var packages = service.QueryPackages();

        Assert.Single(packages);
    }

    [Fact]
    public void QueryPackages_DeduplicatesInstallLocation()
    {
        var processExecution = CreateProcessExecutionService("""
            [{"PackageFamilyName":"Contoso.App_8wekyb3d8bbwe","PackageFullName":"Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe","InstallLocation":"C:\\Program Files\\WindowsApps\\Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe"},{"PackageFamilyName":"Contoso.App_8wekyb3d8bbwe","PackageFullName":"Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe","InstallLocation":"C:\\Program Files\\WindowsApps\\Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe"}]
            """);
        var service = new PowerShellAppxPackageQueryService(processExecution.Object);

        var packages = service.QueryPackages();

        Assert.Single(packages);
    }

    [Fact]
    public void QueryPackages_StartFailure_ReturnsEmpty()
    {
        var processExecution = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        processExecution
            .Setup(service => service.Run(It.IsAny<ProcessExecutionRequest>()))
            .Returns(new ProcessExecutionResult(false, null, false, string.Empty, string.Empty, "failed"));
        var service = new PowerShellAppxPackageQueryService(processExecution.Object);

        var packages = service.QueryPackages();

        Assert.Empty(packages);
    }

    [Fact]
    public void QueryPackages_AllUsersFailure_FallsBackToCurrentUser()
    {
        var processExecution = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        var requests = new List<ProcessExecutionRequest>();
        var results = new Queue<ProcessExecutionResult>(
        [
            new(false, null, false, string.Empty, string.Empty, "denied"),
            new(
                true,
                0,
                false,
                """{"PackageFamilyName":"Contoso.App_8wekyb3d8bbwe","PackageFullName":"Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe","InstallLocation":"C:\\Program Files\\WindowsApps\\Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe"}""",
                string.Empty,
                null)
        ]);
        processExecution
            .Setup(service => service.Run(It.IsAny<ProcessExecutionRequest>()))
            .Returns<ProcessExecutionRequest>(request =>
            {
                requests.Add(request);
                return results.Dequeue();
            });
        var service = new PowerShellAppxPackageQueryService(processExecution.Object);

        var packages = service.QueryPackages();

        Assert.Single(packages);
        Assert.Collection(
            requests,
            first => Assert.Contains("Get-AppxPackage -AllUsers", DecodeEncodedCommand(first.Arguments), StringComparison.Ordinal),
            second =>
            {
                var script = DecodeEncodedCommand(second.Arguments);
                Assert.Contains("Get-AppxPackage", script, StringComparison.Ordinal);
                Assert.DoesNotContain("Get-AppxPackage -AllUsers", script, StringComparison.Ordinal);
                Assert.DoesNotContain("-AllUsers", script, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void QueryPackages_InvalidJson_ReturnsEmpty()
    {
        var processExecution = CreateProcessExecutionService("not json");
        var service = new PowerShellAppxPackageQueryService(processExecution.Object);

        var packages = service.QueryPackages();

        Assert.Empty(packages);
    }

    private static Mock<IProcessExecutionService> CreateProcessExecutionService(string stdout)
    {
        var processExecution = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        processExecution
            .Setup(service => service.Run(It.IsAny<ProcessExecutionRequest>()))
            .Returns(new ProcessExecutionResult(true, 0, false, stdout, string.Empty, null));
        return processExecution;
    }

    private static string DecodeEncodedCommand(string arguments)
    {
        const string marker = "-EncodedCommand ";
        var markerIndex = arguments.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Expected '{marker}' in process arguments.");
        var encodedCommand = arguments[(markerIndex + marker.Length)..].Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(encodedCommand));
    }
}
