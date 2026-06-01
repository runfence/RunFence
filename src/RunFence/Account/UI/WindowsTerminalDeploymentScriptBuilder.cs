namespace RunFence.Account.UI;

public sealed class WindowsTerminalDeploymentScriptBuilder
{
    private const string InstallationTitle = "Installing shared Windows Terminal from official GitHub";

    public string BuildDownloadScript(WindowsTerminalPackageDownloadOperation operation)
        => $"$Host.UI.RawUI.WindowTitle = {ToPowerShellSingleQuotedLiteral(InstallationTitle)}\n" +
           $"Write-Host {ToPowerShellSingleQuotedLiteral(InstallationTitle)}\n" +
           "$ErrorActionPreference = 'Stop'\n" +
           $"Write-Host \"> Invoke-WebRequest {ToPowerShellSingleQuotedLiteral(operation.DownloadUrl)}\"\n" +
           $"Invoke-WebRequest -Uri {ToPowerShellSingleQuotedLiteral(operation.DownloadUrl)} -OutFile {ToPowerShellSingleQuotedLiteral(operation.DestinationPath)}\n";

    public string BuildDeploymentScript(WindowsTerminalDeploymentOperation operation)
    {
        return
            $"$Host.UI.RawUI.WindowTitle = {ToPowerShellSingleQuotedLiteral(InstallationTitle)}\n" +
            $"Write-Host {ToPowerShellSingleQuotedLiteral(InstallationTitle)}\n" +
            "$ErrorActionPreference = 'Stop'\n" +
            $"$cachedZipPath = {ToPowerShellSingleQuotedLiteral(operation.CachedZipPath)}\n" +
            $"$stagingRootPath = {ToPowerShellSingleQuotedLiteral(operation.StagingRootPath)}\n" +
            $"$extractRootPath = {ToPowerShellSingleQuotedLiteral(operation.ExtractRootPath)}\n" +
            $"$expectedVersion = {ToPowerShellSingleQuotedLiteral(operation.ExpectedVersion.ToString())}\n" +
            $"$deploymentVersionFileName = {ToPowerShellSingleQuotedLiteral(operation.DeploymentVersionFileName)}\n" +
            "if (-not (Test-Path -LiteralPath $stagingRootPath -PathType Container)) { throw 'Managed staging directory is missing.' }\n" +
            "if (-not (Test-Path -LiteralPath $extractRootPath -PathType Container)) { throw 'Managed extract directory is missing.' }\n" +
            "if ((Get-ChildItem -LiteralPath $stagingRootPath -Force | Select-Object -First 1) -ne $null) { throw 'Managed staging directory is not empty.' }\n" +
            "if ((Get-ChildItem -LiteralPath $extractRootPath -Force | Select-Object -First 1) -ne $null) { throw 'Managed extract directory is not empty.' }\n" +
            "    Write-Host \"> Expand-Archive\"\n" +
            "    Expand-Archive -LiteralPath $cachedZipPath -DestinationPath $extractRootPath -Force\n" +
            "    $innerDirectories = @(Get-ChildItem -LiteralPath $extractRootPath -Directory | Where-Object { $_.Name -like 'terminal-*' })\n" +
            "    if ($innerDirectories.Count -ne 1) { throw 'Expected one terminal-* directory in the Windows Terminal ZIP.' }\n" +
            "    $payloadRoot = $innerDirectories[0].FullName\n" +
            "    Get-ChildItem -LiteralPath $payloadRoot -Force | ForEach-Object {\n" +
            "        Move-Item -LiteralPath $_.FullName -Destination $stagingRootPath\n" +
            "    }\n" +
            "    if (-not (Test-Path -LiteralPath (Join-Path $stagingRootPath 'WindowsTerminal.exe'))) {\n" +
            "        throw 'WindowsTerminal.exe was not found in the extracted payload.'\n" +
            "    }\n" +
            "    Set-Content -LiteralPath (Join-Path $stagingRootPath $deploymentVersionFileName) -Value $expectedVersion -Encoding ASCII\n";
    }

    private static string ToPowerShellSingleQuotedLiteral(string value)
        => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
