using Xunit;

namespace RunFence.Tests;

public sealed class WindowNativeWrapperTests
{
    [Fact]
    public void WindowNative_AllowsOleAndDropFilesMessagesFromLowIl()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RunFence", "Infrastructure", "WindowNative.cs");
        var source = File.ReadAllText(Path.GetFullPath(sourcePath));

        Assert.Contains("AllowDropFilesFromLowIL", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDropFromLowIL", source, StringComparison.Ordinal);
        Assert.Contains("ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero)", source, StringComparison.Ordinal);
        Assert.Contains("ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero)", source, StringComparison.Ordinal);
        Assert.Contains("ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowSecurityService_UsesFileDropHelper()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RunFence", "Infrastructure", "WindowSecurityService.cs");
        var source = File.ReadAllText(Path.GetFullPath(sourcePath));

        Assert.DoesNotContain("AllowDropFilesFromLowIL", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDropFromLowIL", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DropFilesInterceptor_EnablesFileDropHelperOnTargetHwnd()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RunFence", "Infrastructure", "DropFilesInterceptor.cs");
        var source = File.ReadAllText(Path.GetFullPath(sourcePath));

        Assert.Contains("AllowDropFilesFromLowIL(hwnd)", source, StringComparison.Ordinal);
        Assert.Contains("ShellNative.DragAcceptFiles(hwnd, true)", source, StringComparison.Ordinal);
    }
}