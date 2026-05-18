namespace RunFence.Wizard.Templates;

public record PrepareSystemDriveInfo(
    string RootPath,
    bool IsReady,
    string? DriveFormat,
    long? TotalSize,
    string? InspectionError = null);
