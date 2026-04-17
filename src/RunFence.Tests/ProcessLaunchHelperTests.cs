using RunFence.Core.Models;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public class ProcessLaunchHelperTests
{
    [Theory]
    [InlineData("--default", false, "--launcher-args", "--default")] // AllowPassing=false ignores launcher args
    [InlineData("--default", true, "--launcher-args", "--launcher-args")] // AllowPassing=true uses launcher args
    [InlineData("--default", true, null, "--default")] // AllowPassing=true, null launcher args → fallback to default
    [InlineData("--default", false, null, "--default")] // AllowPassing=false, null launcher args → default
    [InlineData("", false, "--launcher-args", null)] // Empty default with AllowPassing=false → null (no args to use)
    public void DetermineArguments_ReturnsCorrectArgs(
        string defaultArgs, bool allowPassing, string? launcherArg, string? expected)
    {
        var app = new AppEntry { DefaultArguments = defaultArgs, AllowPassingArguments = allowPassing };

        var result = ProcessLaunchHelper.DetermineArguments(app, launcherArg);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(@"C:\stored", false, @"C:\launcher", @"C:\stored")] // AllowPassing=false → always use stored
    [InlineData(@"C:\stored", true, @"C:\launcher", @"C:\launcher")] // AllowPassing=true, launcher provided → use launcher
    [InlineData(@"C:\stored", true, null, @"C:\stored")] // AllowPassing=true, null launcher → fallback to stored
    [InlineData(@"C:\stored", true, "   ", @"C:\stored")] // AllowPassing=true, whitespace-only launcher → fallback to stored
    [InlineData(null, false, @"C:\launcher", null)] // AllowPassing=false, no stored → null
    [InlineData(null, true, @"C:\launcher", @"C:\launcher")] // AllowPassing=true, null stored → use launcher
    [InlineData(null, true, null, null)] // AllowPassing=true, nothing provided → null
    public void DetermineWorkingDirectory_ReturnsCorrectDirectory(
        string? storedWorkDir, bool allowPassing, string? launcherWorkDir, string? expected)
    {
        var app = new AppEntry { WorkingDirectory = storedWorkDir, AllowPassingWorkingDirectory = allowPassing };

        var result = ProcessLaunchHelper.DetermineWorkingDirectory(app, launcherWorkDir);

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

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void WrapTargetForLaunch_Script_UnsafePath_ThrowsInvalidOperation(string ext)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProcessLaunchHelper.WrapTargetForLaunch(new ProcessLaunchTarget($"C:\\test&evil{ext}")));
    }

    [Theory]
    [InlineData(".msi")]
    [InlineData(".reg")]
    [InlineData(".lnk")]
    public void WrapTargetForLaunch_NonScript_SpecialCharsInPath_UsesRundll32AndDoesNotThrow(string ext)
    {
        var (wrappedTarget, isWrapped) = ProcessLaunchHelper.WrapTargetForLaunch(
            new ProcessLaunchTarget($"C:\\test&evil{ext}"));

        Assert.True(isWrapped);
        Assert.Equal("rundll32.exe", wrappedTarget.ExePath);
    }

    // --- .ps1 tests ---

    [Fact]
    public void WrapTargetForLaunch_Ps1_UsesPowershellWithExecutionPolicyBypassAndFile()
    {
        var (wrappedTarget, isWrapped) = ProcessLaunchHelper.WrapTargetForLaunch(
            new ProcessLaunchTarget(@"C:\scripts\deploy.ps1"));

        Assert.False(isWrapped);
        Assert.Equal("powershell.exe", wrappedTarget.ExePath);
        Assert.Equal(@"-ExecutionPolicy Bypass -File ""C:\scripts\deploy.ps1""", wrappedTarget.Arguments);
    }

    [Fact]
    public void WrapTargetForLaunch_Ps1_WithArguments_AppendedVerbatim()
    {
        // Arguments must not be cmd-escaped — powershell.exe is launched directly, not via cmd.exe
        var (wrappedTarget, _) = ProcessLaunchHelper.WrapTargetForLaunch(
            new ProcessLaunchTarget(@"C:\scripts\run.ps1", Arguments: "-Name \"foo bar\" -Count 3"));

        Assert.Equal(@"-ExecutionPolicy Bypass -File ""C:\scripts\run.ps1"" -Name ""foo bar"" -Count 3", wrappedTarget.Arguments);
    }

    [Fact]
    public void WrapTargetForLaunch_Ps1_ArgumentsWithCmdMetachars_NotEscaped()
    {
        // & | % are valid in PS args; they must not be ^-escaped
        var (wrappedTarget, _) = ProcessLaunchHelper.WrapTargetForLaunch(
            new ProcessLaunchTarget(@"C:\scripts\run.ps1", Arguments: "-Url http://a.com?x=1&y=2 -Pct 50%"));

        Assert.Contains("&y=2", wrappedTarget.Arguments);
        Assert.Contains("50%", wrappedTarget.Arguments);
        Assert.DoesNotContain("^", wrappedTarget.Arguments);
    }

    [Fact]
    public void WrapTargetForLaunch_Ps1_SpecialCharsInPath_DoesNotThrow()
    {
        // .ps1 is not launched via cmd.exe, so & in path does not require IsPathSafeForCmd
        var (wrappedTarget, isWrapped) = ProcessLaunchHelper.WrapTargetForLaunch(
            new ProcessLaunchTarget(@"C:\test&evil.ps1"));

        Assert.False(isWrapped);
        Assert.Equal("powershell.exe", wrappedTarget.ExePath);
    }

    [Fact]
    public void WrapTargetForLaunch_NonExeNonScript_UsesRundll32ShellExecRunDLL()
    {
        var (wrappedTarget, isWrapped) = ProcessLaunchHelper.WrapTargetForLaunch(
            new ProcessLaunchTarget(@"C:\docs\report.pdf"));

        Assert.True(isWrapped);
        Assert.Equal("rundll32.exe", wrappedTarget.ExePath);
        Assert.False(wrappedTarget.HideWindow);
        Assert.Equal(@"shell32.dll,ShellExec_RunDLL C:\docs\report.pdf", wrappedTarget.Arguments);
    }

    [Fact]
    public void WrapTargetForLaunch_NonExeNonScript_PathPassedVerbatim()
    {
        var (wrappedTarget, _) = ProcessLaunchHelper.WrapTargetForLaunch(
            new ProcessLaunchTarget(@"C:\my 'docs'\file name.txt"));

        Assert.Equal(@"shell32.dll,ShellExec_RunDLL C:\my 'docs'\file name.txt", wrappedTarget.Arguments);
    }

    [Theory]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void WrapTargetForLaunch_Script_UsesCmdExe(string ext)
    {
        var (wrappedTarget, isWrapped) = ProcessLaunchHelper.WrapTargetForLaunch(
            new ProcessLaunchTarget($@"C:\scripts\run{ext}"));

        Assert.False(isWrapped);
        Assert.Equal("cmd.exe", wrappedTarget.ExePath);
        Assert.StartsWith("/c ", wrappedTarget.Arguments);
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

    [Theory]
    [InlineData("\"", "\\\"")] // Standalone " → \"
    [InlineData("\\\\\"", "\\\\\\\\\\\"")] // Two backslashes before " → four backslashes + \"
    [InlineData(@"C:\path\file", @"C:\path\file")] // Backslashes not followed by " are emitted as-is
    public void SanitizeForSubstitution_EscapesCorrectly(string input, string expected)
    {
        Assert.Equal(expected, ProcessLaunchHelper.SanitizeForSubstitution(input));
    }

    // --- associationArgsTemplate parameter ---

    [Fact]
    public void DetermineArguments_NonNullAssociationTemplate_OverridesAppTemplate()
    {
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            DefaultArguments = "--default",
            ArgumentsTemplate = "--app \"%1\""
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, "https://example.com", "--assoc \"%1\"");

        Assert.Equal("--assoc \"https://example.com\"", result);
    }

    [Fact]
    public void DetermineArguments_NullAssociationTemplate_FallsBackToAppTemplate()
    {
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            DefaultArguments = "--default",
            ArgumentsTemplate = "--app \"%1\""
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, "https://example.com", null);

        Assert.Equal("--app \"https://example.com\"", result);
    }

    [Fact]
    public void DetermineArguments_EmptyAssociationTemplate_DoesNotFallBackToAppTemplate()
    {
        // Empty string = "association launch with no template" → plain replace, NOT app template fallback
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            DefaultArguments = "--default",
            ArgumentsTemplate = "--app \"%1\""
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, "https://example.com", "");

        Assert.Equal("https://example.com", result);
    }

    [Fact]
    public void DetermineArguments_AssociationTemplate_NullLauncherArgs_ReturnsDefaultArgs()
    {
        var app = new AppEntry
        {
            AllowPassingArguments = true,
            DefaultArguments = "--default",
            ArgumentsTemplate = "--app \"%1\""
        };

        var result = ProcessLaunchHelper.DetermineArguments(app, null, "--assoc \"%1\"");

        Assert.Equal("--default", result);
    }

    // ── LaunchUrl tests ───────────────────────────────────────────────────

    [Fact]
    public void BuildUrlLaunchTarget_InvalidScheme_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProcessLaunchHelper.BuildUrlLaunchTarget("file://C:/secret"));
    }

    [Fact]
    public void BuildUrlLaunchTarget_ValidScheme_ReturnsRundll32Target()
    {
        var target = ProcessLaunchHelper.BuildUrlLaunchTarget("steam://run/12345");

        Assert.Equal("rundll32.exe", target.ExePath);
        Assert.Contains("url.dll,FileProtocolHandler", target.Arguments);
    }

    [Fact]
    public void BuildUrlLaunchTarget_UrlPassedVerbatim_InPsiArguments()
    {
        var target = ProcessLaunchHelper.BuildUrlLaunchTarget("myapp://a?x=1&y=2");

        Assert.Contains("myapp://a?x=1&y=2", target.Arguments);
        Assert.DoesNotContain("^&", target.Arguments);
    }
}
