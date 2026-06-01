using RunFence.Infrastructure;
using RunFence.Core;

namespace RunFence.UI;

public sealed class ApplicationCaptionTextBuilder
{
    private const int MaxNotifyIconTextLength = 63;
    private const string TrayAppName = "RunFence";
    private const string ForegroundSuffixSeparator = " - ";
    private const string ProcessAsAccountSeparator = " as ";
    private const string ModeSuffixSeparator = " ";
    private const string Ellipsis = "...";
    private const int MinEllipsisLength = 3;

    public string BuildMainFormTitle(bool isLicensed)
    {
        var title = isLicensed ? TrayAppName : $"{TrayAppName} (Evaluation)";
        if (DebugHelper.UseAdminOperationMocks)
            title += " [NON-ELEVATED]";
        if (!string.IsNullOrEmpty(DebugHelper.AppId))
            title += $" [{DebugHelper.AppId}]";
        return title;
    }

    public string BuildBaseTrayTooltip() => BuildBaseTooltipWithoutSuffix();

    public string BuildForegroundMarkerTrayTooltip(string processName, string accountName, string? modeLabel)
    {
        var baseTooltip = BuildBaseTooltipWithoutSuffix();
        var mode = string.IsNullOrWhiteSpace(modeLabel) ? string.Empty : $"{ModeSuffixSeparator}{modeLabel}";
        var availableForSuffix = MaxNotifyIconTextLength - baseTooltip.Length - mode.Length;

        if (availableForSuffix <= 0)
            return TruncateWithMode(baseTooltip, mode);

        var foregroundSuffix = BuildForegroundSuffix(processName, accountName, availableForSuffix);
        if (string.IsNullOrEmpty(foregroundSuffix))
            return TruncateWithMode(baseTooltip, mode);

        var result = $"{baseTooltip}{foregroundSuffix}{mode}";

        if (result.Length <= MaxNotifyIconTextLength)
            return result;

        return TruncateWithMode(baseTooltip + foregroundSuffix, mode);
    }

    private static string BuildBaseTooltipWithoutSuffix()
    {
        var tooltip = TrayAppName;
        if (DebugHelper.UseAdminOperationMocks)
            tooltip += " [NON-ELEVATED]";
        if (!string.IsNullOrEmpty(DebugHelper.AppId))
            tooltip += $" [{DebugHelper.AppId}]";
        return tooltip;
    }

    private static string BuildForegroundSuffix(
        string processName,
        string accountName,
        int suffixBudget)
    {
        if (suffixBudget <= ForegroundSuffixSeparator.Length)
            return string.Empty;

        var namesBudget = suffixBudget - ForegroundSuffixSeparator.Length;

        if (string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(accountName))
            return string.Empty;

        if (string.IsNullOrEmpty(processName))
        {
            var accountOnly = TruncateName(accountName, namesBudget);
            return accountOnly.Length == 0 ? string.Empty : $"{ForegroundSuffixSeparator}{accountOnly}";
        }

        if (string.IsNullOrEmpty(accountName))
        {
            var processOnly = TruncateName(processName, namesBudget);
            return processOnly.Length == 0 ? string.Empty : $"{ForegroundSuffixSeparator}{processOnly}";
        }

        var fullSuffix = $"{ForegroundSuffixSeparator}{processName}{ProcessAsAccountSeparator}{accountName}";
        if (fullSuffix.Length <= suffixBudget)
            return fullSuffix;

        var bothNamesCanBeEmitted = namesBudget >= 1 + ProcessAsAccountSeparator.Length + 1;
        if (!bothNamesCanBeEmitted)
        {
            var accountOnly = TruncateName(accountName, namesBudget);
            return accountOnly.Length == 0 ? string.Empty : $"{ForegroundSuffixSeparator}{accountOnly}";
        }

        var fullAccountFitsWithMinimumProcess = accountName.Length <= namesBudget - ProcessAsAccountSeparator.Length - 1;
        if (fullAccountFitsWithMinimumProcess)
        {
            var processBudgetForFullAccount = Math.Max(1, namesBudget - ProcessAsAccountSeparator.Length - accountName.Length);
            var processTextForFullAccount = TruncateName(processName, processBudgetForFullAccount);
            return $"{ForegroundSuffixSeparator}{processTextForFullAccount}{ProcessAsAccountSeparator}{accountName}";
        }

        if (processName.Length + ProcessAsAccountSeparator.Length + 1 <= namesBudget)
        {
            var processTextKeepFull = processName;
            var accountBudgetToKeepFullProcess = namesBudget - processTextKeepFull.Length - ProcessAsAccountSeparator.Length;
            var accountForFullProcess = TruncateName(accountName, accountBudgetToKeepFullProcess);
            return $"{ForegroundSuffixSeparator}{processTextKeepFull}{ProcessAsAccountSeparator}{accountForFullProcess}";
        }

        var minProcessBudget = 1;
        if (minProcessBudget > processName.Length)
            minProcessBudget = processName.Length;

        var accountBudgetForMinProcess = Math.Max(1, namesBudget - minProcessBudget - ProcessAsAccountSeparator.Length);
        if (minProcessBudget < 1)
            return string.Empty;
        var processText = TruncateName(processName, minProcessBudget);
        var account = TruncateName(accountName, accountBudgetForMinProcess);
        if (string.IsNullOrEmpty(processText) || string.IsNullOrEmpty(account))
            return string.Empty;

        return $"{ForegroundSuffixSeparator}{processText}{ProcessAsAccountSeparator}{account}";
    }

    private static string TruncateWithMode(string prefix, string mode)
    {
        if (mode.Length == 0)
            return TruncateName(prefix, MaxNotifyIconTextLength);

        var baseBudget = MaxNotifyIconTextLength - mode.Length;
        if (baseBudget <= 0)
            return mode[..MaxNotifyIconTextLength];

        return $"{TruncateName(prefix, baseBudget)}{mode}";
    }

    private static string TruncateName(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        if (maxLength <= 0)
            return string.Empty;

        if (maxLength <= MinEllipsisLength)
            return new string('.', maxLength);

        return $"{value[..(maxLength - 3)]}{Ellipsis}";
    }
}
