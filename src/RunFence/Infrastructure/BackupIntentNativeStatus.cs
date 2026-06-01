namespace RunFence.Infrastructure;

public static class BackupIntentNativeStatus
{
    public const int StatusNoMoreFiles = unchecked((int)0x80000006);
    public const int StatusAccessDenied = unchecked((int)0xC0000022);
    public const int StatusSharingViolation = unchecked((int)0xC0000043);
    public const int StatusPrivilegeNotHeld = unchecked((int)0xC0000061);
    public const int StatusDeletePending = unchecked((int)0xC0000056);
    public const int StatusObjectNameNotFound = unchecked((int)0xC0000034);
    public const int StatusObjectPathNotFound = unchecked((int)0xC000003A);
    public const int StatusObjectPathInvalid = unchecked((int)0xC0000039);
    public const int StatusNoSuchFile = unchecked((int)0xC000000F);
    public const int StatusNoSuchDevice = unchecked((int)0xC000000E);
    public const int StatusNotADirectory = unchecked((int)0xC0000103);
    public const int StatusFileIsADirectory = unchecked((int)0xC00000BA);
    public const int StatusCannotDelete = unchecked((int)0xC0000121);
}
