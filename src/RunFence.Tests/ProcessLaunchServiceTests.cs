using System.Diagnostics;
using System.Security;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public class ProcessLaunchServiceTests
{
    private readonly ProcessLaunchService _service;

    public ProcessLaunchServiceTests()
    {
        var log = new Mock<ILoggingService>();
        _service = new ProcessLaunchService(log.Object,
            new Mock<ISplitTokenLauncher>().Object,
            new Mock<ILowIntegrityLauncher>().Object,
            new Mock<IInteractiveUserLauncher>().Object,
            new Mock<ICurrentAccountLauncher>().Object,
            new Mock<IInteractiveLogonHelper>().Object);
    }

    private static (ProcessLaunchService service, Mock<ISplitTokenLauncher> splitToken,
        Mock<ILowIntegrityLauncher> lowIntegrity, Mock<IInteractiveUserLauncher> interactiveUser,
        Mock<ICurrentAccountLauncher> currentAccount)
        CreateServiceWithMockedLaunchers()
    {
        var log = new Mock<ILoggingService>();
        var splitToken = new Mock<ISplitTokenLauncher>();
        var lowIntegrity = new Mock<ILowIntegrityLauncher>();
        var interactiveUser = new Mock<IInteractiveUserLauncher>();
        var currentAccount = new Mock<ICurrentAccountLauncher>();
        currentAccount.Setup(c => c.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>())).Returns(99);
        var service = new ProcessLaunchService(log.Object, splitToken.Object, lowIntegrity.Object,
            interactiveUser.Object, currentAccount.Object, new Mock<IInteractiveLogonHelper>().Object);
        return (service, splitToken, lowIntegrity, interactiveUser, currentAccount);
    }

    [Theory]
    [InlineData("--default", false, "--launcher-args", "--default")] // AllowPassing=false ignores launcher args
    [InlineData("--default", true, "--launcher-args", "--launcher-args")] // AllowPassing=true uses launcher args
    [InlineData("--default", true, null, "--default")] // AllowPassing=true, null launcher args → default
    [InlineData("--default", true, "", "--default")] // AllowPassing=true, empty launcher args → default
    [InlineData("--default", false, null, "--default")] // AllowPassing=false, null launcher args → default
    [InlineData("", false, "--launcher-args", null)] // Empty default → null result
    public void DetermineArguments_ReturnsCorrectArgs(
        string defaultArgs, bool allowPassing, string? launcherArg, string? expected)
    {
        var app = new AppEntry { DefaultArguments = defaultArgs, AllowPassingArguments = allowPassing };

        var result = ProcessLaunchHelper.DetermineArguments(app, launcherArg == "" ? null : launcherArg);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(@"C:\stored", false, @"C:\launcher", @"C:\stored")] // AllowPassing=false → always use stored
    [InlineData(@"C:\stored", true, @"C:\launcher", @"C:\launcher")] // AllowPassing=true, launcher provided → use launcher
    [InlineData(@"C:\stored", true, null, @"C:\stored")] // AllowPassing=true, null launcher → fallback to stored
    [InlineData(@"C:\stored", true, "", @"C:\stored")] // AllowPassing=true, whitespace launcher → fallback to stored
    [InlineData(null, false, @"C:\launcher", null)] // AllowPassing=false, no stored → null
    [InlineData(null, true, @"C:\launcher", @"C:\launcher")] // AllowPassing=true, null stored → use launcher
    [InlineData(null, true, null, null)] // AllowPassing=true, nothing provided → null
    public void DetermineWorkingDirectory_ReturnsCorrectDirectory(
        string? storedWorkDir, bool allowPassing, string? launcherWorkDir, string? expected)
    {
        var app = new AppEntry { WorkingDirectory = storedWorkDir, AllowPassingWorkingDirectory = allowPassing };
        var launcherDir = launcherWorkDir == "" ? "   " : launcherWorkDir; // whitespace treated as empty

        var result = ProcessLaunchHelper.DetermineWorkingDirectory(app, launcherDir);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("steam://run/123", "steam://run/123")]
    [InlineData("myapp://a?x=1&y=2", "myapp://a?x=1^&y=2")]
    [InlineData("myapp://test|pipe", "myapp://test^|pipe")]
    [InlineData("myapp://has^caret", "myapp://has^^caret")]
    [InlineData("myapp://a<b>c", "myapp://a^<b^>c")]
    [InlineData("myapp://run!now", "myapp://run^!now")]
    [InlineData("myapp://run(test)", "myapp://run^(test^)")]
    public void EscapeCmdMetacharacters_EscapesMetacharacters(string input, string expected)
    {
        Assert.Equal(expected, ProcessLaunchHelper.EscapeCmdMetacharacters(input));
    }

    [Fact]
    public void LaunchExe_CmdScript_UnsafePath_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _service.LaunchExe(new ProcessLaunchTarget("C:\\test&evil.cmd"), new LaunchCredentials(new SecureString(), ".", "user")));
    }

    [Fact]
    public void LaunchExe_BatScript_UnsafePath_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _service.LaunchExe(new ProcessLaunchTarget("C:\\test&evil.bat"), new LaunchCredentials(new SecureString(), ".", "user")));
    }

    [Theory]
    [InlineData(".ps1")]
    [InlineData(".msi")]
    [InlineData(".reg")]
    [InlineData(".lnk")]
    public void LaunchExe_ShellLaunchType_UnsafePath_ThrowsInvalidOperation(string ext)
    {
        Assert.Throws<InvalidOperationException>(() =>
            _service.LaunchExe(new ProcessLaunchTarget($"C:\\test&evil{ext}"), new LaunchCredentials(new SecureString(), ".", "user")));
    }

    [Fact]
    public void LaunchWithSplitToken_WhenSplitTokenReturnsMinusOne_FallsThroughToLowIntegrityLauncher()
    {
        // Arrange
        var (service, splitToken, lowIntegrity, _, _) = CreateServiceWithMockedLaunchers();
        splitToken.Setup(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()))
            .Returns(-1);

        // Act
        service.LaunchExe(new ProcessLaunchTarget(@"C:\app.exe"),
            new LaunchCredentials(new SecureString(), ".", "user"),
            new LaunchFlags(UseSplitToken: true, UseLowIntegrity: true));

        // Assert: split token was attempted, and low-integrity launcher was invoked as fallback
        splitToken.Verify(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), true, LaunchTokenSource.Credentials,
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()), Times.Once);
        lowIntegrity.Verify(l => l.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), LaunchTokenSource.Credentials,
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void LaunchWithSplitToken_WhenSplitTokenSucceeds_NoFallbackLaunchOccurs()
    {
        // Arrange
        var (service, splitToken, lowIntegrity, interactiveUser, _) = CreateServiceWithMockedLaunchers();
        splitToken.Setup(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()))
            .Returns(42);

        // Act
        service.LaunchExe(new ProcessLaunchTarget(@"C:\app.exe"),
            new LaunchCredentials(new SecureString(), ".", "user"),
            new LaunchFlags(UseSplitToken: true, UseLowIntegrity: true));

        // Assert: split token was used and no fallback launchers were invoked
        splitToken.Verify(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()), Times.Once);
        lowIntegrity.Verify(l => l.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<LaunchTokenSource>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()), Times.Never);
        interactiveUser.Verify(i => i.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void LaunchWithSplitToken_WhenSplitTokenFailsWithCredentials_PsiHasNoPresetCredentialsForSplitTokenAttempt()
    {
        // When split-token is requested, credentials are intentionally NOT pre-set on the PSI (split token
        // launcher handles them internally). When split token returns -1, the fallback path re-applies them.
        // This test verifies that the PSI passed to SplitTokenLauncher has no pre-set credentials, proving
        // the re-application is handled by the fallback path rather than pre-set by the caller.

        // Arrange
        ProcessStartInfo? capturedSplitTokenPsi = null;
        var (service, splitToken, lowIntegrity, _, _) = CreateServiceWithMockedLaunchers();
        splitToken.Setup(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()))
            .Callback((ProcessStartInfo psi, SecureString? _, string? _, string? _, bool _, LaunchTokenSource _, Dictionary<string, string>? _, bool _) =>
                capturedSplitTokenPsi = psi)
            .Returns(-1);

        var password = new SecureString();
        password.AppendChar('x');

        // Act: split token returns -1, low-integrity fallback triggered
        service.LaunchExe(new ProcessLaunchTarget(@"C:\app.exe"),
            new LaunchCredentials(password, "DOMAIN", "alice"),
            new LaunchFlags(UseSplitToken: true, UseLowIntegrity: true));

        // Assert: PSI had no credentials when passed to SplitTokenLauncher (they are its responsibility, not pre-set)
        Assert.NotNull(capturedSplitTokenPsi);
        Assert.True(string.IsNullOrEmpty(capturedSplitTokenPsi!.UserName));
        lowIntegrity.Verify(l => l.Launch(It.IsAny<ProcessStartInfo>(), password, "DOMAIN", "alice",
            LaunchTokenSource.Credentials, It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void LaunchWithSplitToken_WhenSplitTokenFailsWithInteractiveUser_InteractiveUserLauncherInvoked()
    {
        // Arrange
        var (service, splitToken, _, interactiveUser, _) = CreateServiceWithMockedLaunchers();
        splitToken.Setup(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()))
            .Returns(-1);

        // Act
        service.LaunchExe(new ProcessLaunchTarget(@"C:\app.exe"),
            new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser),
            new LaunchFlags(UseSplitToken: true, UseLowIntegrity: false));

        // Assert: interactive user launcher invoked as the normal (non-split-token) fallback
        interactiveUser.Verify(i => i.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void LaunchCurrentAccount_WithoutSplitTokenOrLowIntegrity_UsesCurrentAccountLauncher()
    {
        // CurrentAccountLauncher must be used (not Process.Start) so that extra elevated
        // privileges (SeBackupPrivilege etc.) are removed from the token before launch.
        ProcessStartInfo? capturedPsi = null;
        var (service, splitToken, lowIntegrity, interactiveUser, currentAccount) = CreateServiceWithMockedLaunchers();
        currentAccount.Setup(c => c.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<Dictionary<string, string>?>()))
            .Callback((ProcessStartInfo psi, Dictionary<string, string>? _) => capturedPsi = psi)
            .Returns(99);

        service.LaunchExe(new ProcessLaunchTarget(@"C:\app.exe"),
            new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess));

        currentAccount.Verify(c => c.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<Dictionary<string, string>?>()), Times.Once);
        splitToken.Verify(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
            It.IsAny<Dictionary<string, string>?>()), Times.Never);
        lowIntegrity.Verify(l => l.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<LaunchTokenSource>(),
            It.IsAny<Dictionary<string, string>?>()), Times.Never);
        interactiveUser.Verify(i => i.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        Assert.NotNull(capturedPsi);
        Assert.Equal(@"C:\app.exe", capturedPsi!.FileName);
        Assert.Empty(capturedPsi.Arguments);
    }

    [Fact]
    public void Launch_WithEnvVars_CurrentProcess_PassedToCurrentAccountLauncher()
    {
        // Arrange
        Dictionary<string, string>? capturedEnvVars = null;
        var (service, _, _, _, currentAccount) = CreateServiceWithMockedLaunchers();
        currentAccount.Setup(c => c.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<Dictionary<string, string>?>()))
            .Callback((ProcessStartInfo _, Dictionary<string, string>? env) => capturedEnvVars = env)
            .Returns(99);

        var target = new ProcessLaunchTarget(@"C:\app.exe") { EnvironmentVariables = new() { ["FOO"] = "bar" } };

        // Act
        service.LaunchExe(target, new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess));

        // Assert
        currentAccount.Verify(c => c.Launch(It.IsAny<ProcessStartInfo>(),
            It.Is<Dictionary<string, string>>(d => d["FOO"] == "bar")), Times.Once);
        Assert.NotNull(capturedEnvVars);
        Assert.Equal("bar", capturedEnvVars!["FOO"]);
    }

    [Fact]
    public void Launch_WithEnvVars_InteractiveUser_PassedToInteractiveUserLauncher()
    {
        // Arrange
        Dictionary<string, string>? capturedEnvVars = null;
        var (service, _, _, interactiveUser, _) = CreateServiceWithMockedLaunchers();
        interactiveUser.Setup(i => i.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<Dictionary<string, string>?>()))
            .Callback((ProcessStartInfo _, Dictionary<string, string>? env) => capturedEnvVars = env);

        var target = new ProcessLaunchTarget(@"C:\app.exe") { EnvironmentVariables = new() { ["FOO"] = "bar" } };

        // Act
        service.LaunchExe(target, new LaunchCredentials(null, null, null, LaunchTokenSource.InteractiveUser));

        // Assert
        interactiveUser.Verify(i => i.Launch(It.IsAny<ProcessStartInfo>(),
            It.Is<Dictionary<string, string>>(d => d["FOO"] == "bar")), Times.Once);
        Assert.NotNull(capturedEnvVars);
        Assert.Equal("bar", capturedEnvVars!["FOO"]);
    }

    [Fact]
    public void Launch_WithEnvVars_SplitToken_PassedToSplitTokenLauncher()
    {
        // Arrange
        Dictionary<string, string>? capturedEnvVars = null;
        var password = new SecureString();
        password.AppendChar('p');
        var (service, splitToken, _, _, _) = CreateServiceWithMockedLaunchers();
        splitToken.Setup(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
                It.IsAny<Dictionary<string, string>?>()))
            .Callback((ProcessStartInfo _, SecureString? _, string? _, string? _, bool _, LaunchTokenSource _, Dictionary<string, string>? env) =>
                capturedEnvVars = env)
            .Returns(42);

        var target = new ProcessLaunchTarget(@"C:\app.exe") { EnvironmentVariables = new() { ["FOO"] = "bar" } };

        // Act
        service.LaunchExe(target,
            new LaunchCredentials(password, ".", "user"),
            new LaunchFlags(UseSplitToken: true));

        // Assert
        splitToken.Verify(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
            It.Is<Dictionary<string, string>>(d => d["FOO"] == "bar")), Times.Once);
        Assert.NotNull(capturedEnvVars);
        Assert.Equal("bar", capturedEnvVars!["FOO"]);
    }

    [Fact]
    public void LaunchCurrentAccount_CmdScript_WrapsInCmdExe()
    {
        // CreateProcessWithTokenW (used by CurrentAccountLauncher) cannot launch .cmd/.bat
        // directly — must be wrapped in cmd.exe /c, same as the Credentials path.
        ProcessStartInfo? capturedPsi = null;
        var (service, _, _, _, currentAccount) = CreateServiceWithMockedLaunchers();
        currentAccount.Setup(c => c.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<Dictionary<string, string>?>()))
            .Callback((ProcessStartInfo psi, Dictionary<string, string>? _) => capturedPsi = psi)
            .Returns(99);

        service.LaunchExe(new ProcessLaunchTarget(@"C:\script.cmd"),
            new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess));

        Assert.NotNull(capturedPsi);
        Assert.Equal("cmd.exe", capturedPsi!.FileName);
        Assert.Contains(@"C:\script.cmd", capturedPsi.Arguments);
        Assert.StartsWith("/c \"", capturedPsi.Arguments);
    }

    // --- ArgumentsTemplate tests ---

    [Fact]
    public void DetermineArguments_Template_ReplacesPercentOne()
    {
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            DefaultArguments = "--default",
            ArgumentsTemplate = "--profile \"%1\""
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, "https://example.com");

        Assert.Equal("--profile \"https://example.com\"", result);
    }

    [Fact]
    public void DetermineArguments_Template_NoPercentOne_AppendsArgs()
    {
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            DefaultArguments = "--default",
            ArgumentsTemplate = "--profile custom"
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, "https://example.com");

        Assert.Equal("--profile custom https://example.com", result);
    }

    [Fact]
    public void DetermineArguments_Template_NoPercentOne_AppendsArgs_WhenTemplateEndsWithQuote()
    {
        // Closing quote triggers a space separator (same as alphanumeric)
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            ArgumentsTemplate = "--profile \"custom\""
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, "url.com");

        Assert.Equal("--profile \"custom\" url.com", result);
    }

    [Fact]
    public void DetermineArguments_Template_NoPercentOne_NoDoubleSpaceWhenTemplateEndsWithSpace()
    {
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            ArgumentsTemplate = "--flag "
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, "url.com");

        Assert.Equal("--flag url.com", result);
    }

    [Fact]
    public void DetermineArguments_Template_NullTemplate_ReplacesDefaultArgs()
    {
        // Null template: original replace behavior — passed args replace DefaultArguments entirely
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            DefaultArguments = "--default",
            ArgumentsTemplate = null
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, "https://example.com");

        Assert.Equal("https://example.com", result);
    }

    [Fact]
    public void DetermineArguments_Template_EmptyLauncherArgs_ReturnsDefaultArgs()
    {
        // No launcher args → always return DefaultArguments, regardless of template
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            DefaultArguments = "--default",
            ArgumentsTemplate = "--profile \"%1\""
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, null);

        Assert.Equal("--default", result);
    }

    [Fact]
    public void DetermineArguments_Template_MultiplePercentOne_AllReplaced()
    {
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            ArgumentsTemplate = "--a \"%1\" --b \"%1\""
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, "value");

        Assert.Equal("--a \"value\" --b \"value\"", result);
    }

    [Fact]
    public void DetermineArguments_Template_EscapesQuotesWithMsvcRules()
    {
        // Input containing " should be escaped so it cannot break out of "%1" context
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            ArgumentsTemplate = "\"%1\""
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, "evil\" --flag");

        // " → \" (1 backslash + quote), so result is: "evil\" --flag"
        // CommandLineToArgvW parses this as: evil" --flag (one argument)
        Assert.Equal("\"evil\\\" --flag\"", result);
    }

    [Fact]
    public void DetermineArguments_Template_EscapesBackslashQuoteSequence()
    {
        // N backslashes before " → 2N+1 backslashes + \"
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            ArgumentsTemplate = "\"%1\""
        };

        // Input: foo\"bar (backslash immediately followed by quote)
        var result = ProcessLaunchHelper.DetermineArguments(app, "foo\\\"bar");

        // 1 backslash + " → 2*1+1=3 backslashes + \" in the sanitized value
        Assert.Equal("\"foo\\\\\\\"bar\"", result);
    }

    [Fact]
    public void DetermineArguments_Template_StripsOuterQuotes()
    {
        // Pre-quoted input like "C:\path" — outer quotes stripped before escaping/substitution
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            ArgumentsTemplate = "\"%1\""
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, "\"C:\\path\\to\\file\"");

        Assert.Equal("\"C:\\path\\to\\file\"", result);
    }

    [Fact]
    public void SanitizeForSubstitution_EscapesQuoteWithNoBackslashes()
    {
        // Standalone " → \"
        Assert.Equal("\\\"", ProcessLaunchHelper.SanitizeForSubstitution("\""));
    }

    [Fact]
    public void SanitizeForSubstitution_EscapesBackslashesBeforeQuote()
    {
        // Two backslashes before " → four backslashes + \"
        Assert.Equal("\\\\\\\\\\\"", ProcessLaunchHelper.SanitizeForSubstitution("\\\\\""));
    }

    [Fact]
    public void SanitizeForSubstitution_BackslashesNotBeforeQuotePassThrough()
    {
        // Backslashes not followed by " are emitted as-is
        Assert.Equal(@"C:\path\file", ProcessLaunchHelper.SanitizeForSubstitution(@"C:\path\file"));
    }

    // ── LaunchUrl tests ───────────────────────────────────────────────────

    private static (ProcessLaunchService service, Mock<ISplitTokenLauncher> splitToken,
        Mock<IInteractiveLogonHelper> logonHelper)
        CreateServiceWithLogonHelper()
    {
        var log = new Mock<ILoggingService>();
        var splitToken = new Mock<ISplitTokenLauncher>();
        var lowIntegrity = new Mock<ILowIntegrityLauncher>();
        var interactiveUser = new Mock<IInteractiveUserLauncher>();
        var currentAccount = new Mock<ICurrentAccountLauncher>();
        var logonHelper = new Mock<IInteractiveLogonHelper>();
        var service = new ProcessLaunchService(log.Object, splitToken.Object, lowIntegrity.Object,
            interactiveUser.Object, currentAccount.Object, logonHelper.Object);
        return (service, splitToken, logonHelper);
    }

    [Fact]
    public void LaunchUrl_ValidUrl_Credentials_NoFlags_DispatchesToLogonRetryPath()
    {
        // Arrange: logonHelper default Loose mock returns null for RunWithLogonRetry<Process?>
        var (service, splitToken, logonHelper) = CreateServiceWithLogonHelper();
        var password = new SecureString();
        password.AppendChar('p');

        // Act
        service.LaunchUrl("steam://run/123",
            new LaunchCredentials(password, ".", "user"));

        // Assert: dispatched through logon retry path, split token NOT used
        logonHelper.Verify(l => l.RunWithLogonRetry(
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<Func<Process?>>()), Times.Once);
        splitToken.Verify(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
            It.IsAny<Dictionary<string, string>?>()), Times.Never);
    }

    [Fact]
    public void LaunchUrl_WithSplitToken_UsesSplitTokenLauncher()
    {
        // Arrange
        ProcessStartInfo? capturedPsi = null;
        var (service, splitToken, _) = CreateServiceWithLogonHelper();
        splitToken.Setup(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
                It.IsAny<Dictionary<string, string>?>()))
            .Callback((ProcessStartInfo psi, SecureString? _, string? _, string? _, bool _, LaunchTokenSource _, Dictionary<string, string>? _) =>
                capturedPsi = psi)
            .Returns(42);

        // Act
        service.LaunchUrl("steam://run/123",
            new LaunchCredentials(new SecureString(), ".", "user"),
            new LaunchFlags(UseSplitToken: true));

        // Assert: split token launcher was used, PSI wraps via cmd.exe /c start
        splitToken.Verify(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
            It.IsAny<Dictionary<string, string>?>()), Times.Once);
        Assert.NotNull(capturedPsi);
        Assert.Equal("cmd.exe", capturedPsi!.FileName);
        Assert.Contains("start \"\"", capturedPsi.Arguments);
    }

    [Fact]
    public void LaunchUrl_MetacharactersEscaped_InPsiArguments()
    {
        // Arrange: URL with & which is a cmd.exe metacharacter
        ProcessStartInfo? capturedPsi = null;
        var (service, splitToken, _) = CreateServiceWithLogonHelper();
        splitToken.Setup(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
                It.IsAny<Dictionary<string, string>?>()))
            .Callback((ProcessStartInfo psi, SecureString? _, string? _, string? _, bool _, LaunchTokenSource _, Dictionary<string, string>? _) =>
                capturedPsi = psi)
            .Returns(42);

        // Act
        service.LaunchUrl("myapp://a?x=1&y=2",
            new LaunchCredentials(new SecureString(), ".", "user"),
            new LaunchFlags(UseSplitToken: true));

        // Assert: & is escaped to ^&
        Assert.NotNull(capturedPsi);
        Assert.Contains("^&", capturedPsi!.Arguments);
    }

    [Fact]
    public void LaunchUrl_InvalidScheme_ThrowsInvalidOperation()
    {
        var (service, _, _) = CreateServiceWithLogonHelper();

        Assert.Throws<InvalidOperationException>(() =>
            service.LaunchUrl("file://C:/secret",
                new LaunchCredentials(new SecureString(), ".", "user")));
    }

    // ── T3: Credentials + ExtraEnvVars path ──────────────────────────────

    [Fact]
    public void Launch_WithEnvVars_Credentials_NoFlags_ReachesLogonRetryPath()
    {
        // Arrange: mock RunWithLogonRetry<IntPtr> to return fake token (1) for AcquireLogonToken;
        // RunWithLogonRetry<Process?> returns null by default (Moq Loose).
        var (service, splitToken, logonHelper) = CreateServiceWithLogonHelper();
        var password = new SecureString();
        password.AppendChar('p');
        logonHelper.Setup(l => l.RunWithLogonRetry(
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Func<IntPtr>>()))
            .Returns(new IntPtr(1));

        var target = new ProcessLaunchTarget(@"C:\app.exe")
            { EnvironmentVariables = new() { ["FOO"] = "bar" } };

        // Act: AcquireLogonToken calls RunWithLogonRetry<IntPtr>, then final dispatch calls RunWithLogonRetry<Process?>
        service.LaunchExe(target, new LaunchCredentials(password, ".", "user"));

        // Assert: token acquisition path hit once, final launch path hit once
        logonHelper.Verify(l => l.RunWithLogonRetry(
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<Func<IntPtr>>()), Times.Once);
        logonHelper.Verify(l => l.RunWithLogonRetry(
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<Func<Process?>>()), Times.Once);

        // No split/low-IL/interactive/current launchers used
        splitToken.Verify(s => s.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<SecureString?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<LaunchTokenSource>(),
            It.IsAny<Dictionary<string, string>?>()), Times.Never);
    }
}