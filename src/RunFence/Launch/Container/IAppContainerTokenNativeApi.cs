namespace RunFence.Launch.Container;

public interface IAppContainerTokenNativeApi
{
    IntPtr ConvertRequiredStringSidToSid(string sid);

    bool TryConvertStringSidToSid(string sid, out IntPtr pointer, out int errorCode);

    void LocalFree(IntPtr pointer);

    IntPtr DuplicateToken(IntPtr token);

    IntPtr CreateAppContainerToken(
        IntPtr duplicatedExplorerToken,
        ref AppContainerProcessLauncherNative.SECURITY_CAPABILITIES capabilities);

    void SetRestrictiveDefaultDacl(IntPtr appContainerToken, string containerSid, string interactiveUserSid);

    string GetRequiredTokenSidValue(IntPtr token);

    IntPtr AllocateCapabilityArray(IReadOnlyList<IntPtr> capabilitySidPointers);

    void FreeCapabilityArray(IntPtr pointer);

    bool TryGetAppContainerNamedObjectPath(IntPtr appContainerToken, out string path, out int errorCode);

    void CloseHandle(IntPtr handle);
}
