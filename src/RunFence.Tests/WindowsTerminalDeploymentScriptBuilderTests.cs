using RunFence.Account.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsTerminalDeploymentScriptBuilderTests
{
    [Fact]
    public void BuildDownloadScript_EscapesSingleQuotesAndUsesLiteralPaths()
    {
        var builder = new WindowsTerminalDeploymentScriptBuilder();
        var operation = new WindowsTerminalPackageDownloadOperation(
            "https://example.invalid/it''s.zip",
            @"C:\temp\it''s.zip");

        var script = builder.BuildDownloadScript(operation);

        Assert.Contains("Invoke-WebRequest", script, StringComparison.Ordinal);
        Assert.Contains("$Host.UI.RawUI.WindowTitle = 'Installing shared Windows Terminal from official GitHub'", script, StringComparison.Ordinal);
        Assert.Contains("Write-Host 'Installing shared Windows Terminal from official GitHub'", script, StringComparison.Ordinal);
        Assert.Contains("'https://example.invalid/it''''s.zip'", script, StringComparison.Ordinal);
        Assert.Contains(@"'C:\temp\it''''s.zip'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDeploymentScript_EmitsExpectedVersionAndDeploymentVersionFileName()
    {
        var builder = new WindowsTerminalDeploymentScriptBuilder();
        var operation = new WindowsTerminalDeploymentOperation(
            @"C:\cache\Microsoft.WindowsTerminal_1.2.3.4_x64.zip",
            @"C:\shared",
            @"C:\work\operation",
            @"C:\work\operation\staging path",
            @"C:\work\operation\extract path",
            @"C:\work\operation\backup",
            new Version(1, 2, 3, 4),
            WindowsTerminalDeploymentPaths.DeploymentVersionFileName);

        var script = builder.BuildDeploymentScript(operation);

        Assert.Contains(@"$cachedZipPath = 'C:\cache\Microsoft.WindowsTerminal_1.2.3.4_x64.zip'", script, StringComparison.Ordinal);
        Assert.Contains(@"$stagingRootPath = 'C:\work\operation\staging path'", script, StringComparison.Ordinal);
        Assert.Contains(@"$extractRootPath = 'C:\work\operation\extract path'", script, StringComparison.Ordinal);
        Assert.Contains("$Host.UI.RawUI.WindowTitle = 'Installing shared Windows Terminal from official GitHub'", script, StringComparison.Ordinal);
        Assert.Contains("Write-Host 'Installing shared Windows Terminal from official GitHub'", script, StringComparison.Ordinal);
        Assert.Contains("$expectedVersion = '1.2.3.4'", script, StringComparison.Ordinal);
        Assert.Contains(
            $"$deploymentVersionFileName = '{WindowsTerminalDeploymentPaths.DeploymentVersionFileName}'",
            script,
            StringComparison.Ordinal);
        Assert.Contains("Expand-Archive -LiteralPath $cachedZipPath -DestinationPath $extractRootPath -Force", script, StringComparison.Ordinal);
        Assert.Contains("Set-Content -LiteralPath (Join-Path $stagingRootPath $deploymentVersionFileName) -Value $expectedVersion -Encoding ASCII", script, StringComparison.Ordinal);
    }
}
