using RunFence.Core;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class ApplicationCaptionTextBuilderTests
{
    [Fact]
    public void BuildMainFormTitle_UnlicensedUsesEvaluationButPreservesDebugSuffixes()
    {
        var builder = new ApplicationCaptionTextBuilder();

        Assert.Equal(
            CurrentExpectedUnlicensedTitle(),
            builder.BuildMainFormTitle(false));
    }

    [Fact]
    public void BuildForegroundMarkerTrayTooltip_AppendsSuffixWithoutLicenseWord()
    {
        var builder = new ApplicationCaptionTextBuilder();

        var tooltip = builder.BuildForegroundMarkerTrayTooltip("chrome.exe", "BrowserUser", null);

        Assert.Equal($"{CurrentExpectedBase()} - chrome.exe as BrowserUser", tooltip);
    }

    [Fact]
    public void BuildForegroundMarkerTrayTooltip_AppendsModeWhenProvided()
    {
        var builder = new ApplicationCaptionTextBuilder();

        Assert.Equal($"{CurrentExpectedBase()} - c.exe as u [Isolated]", builder.BuildForegroundMarkerTrayTooltip("c.exe", "u", "[Isolated]"));
        Assert.Equal($"{CurrentExpectedBase()} - c.exe as u [LowIL]", builder.BuildForegroundMarkerTrayTooltip("c.exe", "u", "[LowIL]"));
        Assert.Equal($"{CurrentExpectedBase()} - c.exe as u [Elevated]", builder.BuildForegroundMarkerTrayTooltip("c.exe", "u", "[Elevated]"));
    }

    [Fact]
    public void BuildForegroundMarkerTrayTooltip_TruncatesLongAccountBeforeProcess()
    {
        var builder = new ApplicationCaptionTextBuilder();
        var baseTooltip = builder.BuildBaseTrayTooltip();
        var account = new string('A', 90);

        var tooltip = builder.BuildForegroundMarkerTrayTooltip("chrome.exe", account, null);

        Assert.True(tooltip.Length <= 63);
        var processStart = baseTooltip.Length + " - ".Length;
        Assert.Equal("chrome.exe", tooltip[processStart..(processStart + "chrome.exe".Length)]);
        var asIndex = tooltip.IndexOf(" as ", StringComparison.Ordinal);
        Assert.True(asIndex > processStart);
        var accountText = tooltip[(asIndex + 4)..];
        Assert.NotEqual(account, accountText);
        Assert.Contains("chrome.exe", tooltip);
        Assert.True(accountText.Length < account.Length);
        Assert.DoesNotContain("Isolated", tooltip);
    }

    [Fact]
    public void BuildForegroundMarkerTrayTooltip_PrioritizesAccountWhenProcessOverflows()
    {
        var builder = new ApplicationCaptionTextBuilder();
        var baseTooltip = builder.BuildBaseTrayTooltip();
        var process = new string('V', 80);
        var account = "BrowserAccountName";

        var tooltip = builder.BuildForegroundMarkerTrayTooltip(process, account, null);

        Assert.True(tooltip.Length <= 63);
        var asIndex = tooltip.IndexOf(" as ", StringComparison.Ordinal);
        var processText = ExtractProcessText(tooltip, baseTooltip, asIndex);
        var accountText = asIndex < 0 ? string.Empty : tooltip[(asIndex + 4)..];
        Assert.True(asIndex > baseTooltip.Length);
        Assert.NotEqual(process, processText);
        Assert.Equal(account, accountText);
        Assert.NotEmpty(processText);
        Assert.True(processText.Length < process.Length);
    }

    [Theory]
    [InlineData("[Isolated]")]
    [InlineData("[LowIL]")]
    [InlineData("[Elevated]")]
    public void BuildForegroundMarkerTrayTooltip_PrioritizesAccountUnderModeWhilePreservingMode(string mode)
    {
        var builder = new ApplicationCaptionTextBuilder();
        var baseTooltip = builder.BuildBaseTrayTooltip();
        var process = "VeryLongProcessNameThatOverflows";
        var account = "BrowserAccountName";
        var modeSuffix = $" {mode}";

        var tooltip = builder.BuildForegroundMarkerTrayTooltip(process, account, mode);

        Assert.True(tooltip.Length <= 63);
        Assert.EndsWith(modeSuffix, tooltip);
        var asIndex = tooltip.IndexOf(" as ", StringComparison.Ordinal);
        if (asIndex < 0)
        {
            var separatorIndex = tooltip.IndexOf(" - ", StringComparison.Ordinal);
            var markerPart = tooltip[(separatorIndex + 3)..^modeSuffix.Length];
            Assert.DoesNotContain(" as ", markerPart);
            Assert.NotEmpty(markerPart);
        }
        else
        {
            var processText = ExtractProcessText(tooltip, baseTooltip, asIndex);
            var accountText = tooltip[(asIndex + 4)..^modeSuffix.Length];
            var namesBudget = 63 - baseTooltip.Length - modeSuffix.Length - " - ".Length;
            var expectedAccountBudget = Math.Min(account.Length, namesBudget - " as ".Length - 1);
            var expectedAccountText = TruncateNameForTest(account, expectedAccountBudget);
            var expectedProcessBudget = namesBudget - " as ".Length - expectedAccountText.Length;
            var expectedProcessText = TruncateNameForTest(process, expectedProcessBudget);

            Assert.Equal(expectedProcessText, processText);
            Assert.Equal(expectedAccountText, accountText);
            Assert.NotEmpty(processText);
            Assert.NotEmpty(accountText);
        }
    }

    [Fact]
    public void BuildForegroundMarkerTrayTooltip_OmitsAsSeparatorWhenAccountIsEmpty()
    {
        var builder = new ApplicationCaptionTextBuilder();

        var tooltip = builder.BuildForegroundMarkerTrayTooltip("chrome.exe", string.Empty, null);

        Assert.True(tooltip.Length <= 63);
        Assert.DoesNotContain(" as ", tooltip);
        Assert.Contains("chrome.exe", tooltip);
        Assert.Contains("- chrome.exe", tooltip);
    }

    [Fact]
    public void BuildForegroundMarkerTrayTooltip_OmitsAsSeparatorWhenProcessIsEmpty()
    {
        var builder = new ApplicationCaptionTextBuilder();

        var tooltip = builder.BuildForegroundMarkerTrayTooltip(string.Empty, "User", null);

        Assert.True(tooltip.Length <= 63);
        Assert.DoesNotContain(" as ", tooltip);
        Assert.EndsWith("User", tooltip);
        Assert.Contains("- User", tooltip);
    }

    [Fact]
    public void BuildForegroundMarkerTrayTooltip_FallsBackToAccountOnlyWhenBothNamesWouldBeEmpty()
    {
        var builder = new ApplicationCaptionTextBuilder();
        var baseTooltip = builder.BuildBaseTrayTooltip();
        var mode = new string('M', Math.Max(1, 63 - baseTooltip.Length - 5));
        var process = new string('V', 80);
        var account = "BrowserAccountName";

        var tooltip = builder.BuildForegroundMarkerTrayTooltip(process, account, mode);

        Assert.True(tooltip.Length <= 63);
        var separatorIndex = tooltip.IndexOf(" - ", StringComparison.Ordinal);
        Assert.True(separatorIndex >= 0);
        Assert.DoesNotContain(" as ", tooltip);
        var modeSuffixLength = mode.Length + 1;
        var accountText = tooltip[(separatorIndex + 3)..^modeSuffixLength];
        Assert.NotEmpty(accountText);
        Assert.True(accountText.Length <= 3);
    }

    [Fact]
    public void BuildForegroundMarkerTrayTooltip_TruncatesToFinalCapWhilePreservingMode()
    {
        var builder = new ApplicationCaptionTextBuilder();
        var process = new string('P', 120);
        var account = new string('A', 120);

        var tooltip = builder.BuildForegroundMarkerTrayTooltip(process, account, "[Isolated]");

        Assert.True(tooltip.Length <= 63);
        Assert.EndsWith("[Isolated]", tooltip);
    }

    [Fact]
    public void BuildForegroundMarkerTrayTooltip_FinalCapKeepsModeSuffixEvenWhenNamesAreTrimmedToMinimum()
    {
        var builder = new ApplicationCaptionTextBuilder();
        var tooltip = builder.BuildForegroundMarkerTrayTooltip(new string('P', 120), new string('A', 120), "[LowIL]");

        Assert.True(tooltip.Length <= 63);
        Assert.EndsWith(" [LowIL]", tooltip);
    }

    private static string CurrentExpectedBase()
    {
        return "RunFence" +
               (DebugHelper.UseAdminOperationMocks ? " [NON-ELEVATED]" : string.Empty) +
               (string.IsNullOrEmpty(DebugHelper.AppId) ? string.Empty : $" [{DebugHelper.AppId}]");
    }

    private static string CurrentExpectedUnlicensedTitle()
    {
        return "RunFence (Evaluation)" +
               (DebugHelper.UseAdminOperationMocks ? " [NON-ELEVATED]" : string.Empty) +
               (string.IsNullOrEmpty(DebugHelper.AppId) ? string.Empty : $" [{DebugHelper.AppId}]");
    }

    private static string ExtractProcessText(string tooltip, string baseTooltip, int asIndex)
    {
        return tooltip[(baseTooltip.Length + " - ".Length)..asIndex];
    }

    private static string TruncateNameForTest(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        if (maxLength <= 0)
            return string.Empty;

        if (maxLength <= 3)
            return new string('.', maxLength);

        return $"{value[..(maxLength - 3)]}...";
    }
}
