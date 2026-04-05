namespace RunFence.Launch;

internal static class FolderHandlerNative
{
    // Shell Windows COM server CLSID registered in HKU to intercept SHOpenFolderAndSelectItems
    public const string ShellWindowsClsidRegistryPath =
        @"Software\Classes\CLSID\{9BA05972-F6A8-11CF-A442-00A0C90A8F39}";
}
