namespace RunFence.Account.UI;

public readonly record struct WindowsTerminalDeploymentOperation(
    string CachedZipPath,
    string SharedRootPath,
    string OperationWorkRootPath,
    string StagingRootPath,
    string ExtractRootPath,
    string BackupRootPath,
    Version ExpectedVersion,
    string DeploymentVersionFileName);
