using Moq;
using RunFence.Core;
using RunFence.Launch;
using RunFence.Launching.Resolution;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsAppsPackageRegistrationRepairerTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public void TryCreateRepairTarget_WindowsAppsAccountTarget_ReturnsPackageRegistrationCommand()
    {
        var packageIdentityResolver = new Mock<IWindowsAppsPackageIdentityResolver>();
        var packageFamilyName = "Microsoft.WindowsNotepad_8wekyb3d8bbwe";
        var target = new ProcessLaunchTarget(NotepadPackageExe());
        packageIdentityResolver
            .Setup(r => r.TryResolvePackageFamilyName(target.ExePath, out packageFamilyName))
            .Returns(true);
        var repairer = new WindowsAppsPackageRegistrationRepairer(
            packageIdentityResolver.Object);

        var result = repairer.TryCreateRepairTarget(target);

        Assert.NotNull(result);
        Assert.Equal("powershell.exe", result!.ExePath);
        Assert.True(result.HideWindow);
        Assert.True(result.SuppressStartupFeedback);
        Assert.Contains("Add-AppxPackage", result.Arguments);
        Assert.Contains("'Microsoft.WindowsNotepad_8wekyb3d8bbwe'", result.Arguments);
    }

    [Fact]
    public void TryCreateRepairTarget_NonWindowsAppsTarget_ReturnsNull()
    {
        var packageIdentityResolver = new Mock<IWindowsAppsPackageIdentityResolver>();
        var packageFamilyName = string.Empty;
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var repairer = new WindowsAppsPackageRegistrationRepairer(
            packageIdentityResolver.Object);

        var result = repairer.TryCreateRepairTarget(target);

        Assert.Null(result);
        packageIdentityResolver.Verify(
            r => r.TryResolvePackageFamilyName(target.ExePath, out packageFamilyName),
            Times.Once);
    }

    [Fact]
    public void TryCreateRepairTarget_ExactAliasPath_UsesResolvedPackageFamilyName()
    {
        var target = new ProcessLaunchTarget(
            @"C:\Users\Target\AppData\Local\Microsoft\WindowsApps\notepad.exe");
        var packageIdentityResolver = new Mock<IWindowsAppsPackageIdentityResolver>();
        var aliasPackageFamilyName = "Microsoft.WindowsNotepad_8wekyb3d8bbwe";
        packageIdentityResolver
            .Setup(r => r.TryResolvePackageFamilyName(target.ExePath, out aliasPackageFamilyName))
            .Returns(true);
        var repairer = new WindowsAppsPackageRegistrationRepairer(
            packageIdentityResolver.Object);

        var result = repairer.TryCreateRepairTarget(target);

        Assert.NotNull(result);
        Assert.True(result!.SuppressStartupFeedback);
        Assert.Contains("'Microsoft.WindowsNotepad_8wekyb3d8bbwe'", result!.Arguments);
    }

    [Fact]
    public void TryCreateRepairTarget_ExactAppDataWindowsAppsPath_IsRepairable()
    {
        var packageIdentityResolver = new Mock<IWindowsAppsPackageIdentityResolver>();
        var packageFamilyName = "Microsoft.WindowsTerminal_8wekyb3d8bbwe";
        var target = new ProcessLaunchTarget(
            @"C:\Users\Target\AppData\Local\Microsoft\WindowsApps\wt.exe");
        packageIdentityResolver
            .Setup(r => r.TryResolvePackageFamilyName(target.ExePath, out packageFamilyName))
            .Returns(true);
        var repairer = new WindowsAppsPackageRegistrationRepairer(
            packageIdentityResolver.Object);

        var result = repairer.TryCreateRepairTarget(target);

        Assert.NotNull(result);
        Assert.True(result!.SuppressStartupFeedback);
        Assert.Contains("'Microsoft.WindowsTerminal_8wekyb3d8bbwe'", result.Arguments);
    }

    [Fact]
    public void TryCreateRepairTarget_BareAliasName_ReturnsNull()
    {
        var packageIdentityResolver = new Mock<IWindowsAppsPackageIdentityResolver>();
        var packageFamilyName = string.Empty;
        var target = new ProcessLaunchTarget("calc.exe");
        var repairer = new WindowsAppsPackageRegistrationRepairer(
            packageIdentityResolver.Object);

        var result = repairer.TryCreateRepairTarget(target);

        Assert.Null(result);
        packageIdentityResolver.Verify(
            r => r.TryResolvePackageFamilyName(target.ExePath, out packageFamilyName),
            Times.Once);
    }

    private static string NotepadPackageExe() =>
        Path.Combine(
            @"C:\Program Files\WindowsApps",
            "Microsoft.WindowsNotepad_11.2512.29.0_x64__8wekyb3d8bbwe",
            "Notepad",
            "Notepad.exe");
}
