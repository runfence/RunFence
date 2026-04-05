using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public static class RegionalSettingsIO
{
    public static RegionalSettings Read()
    {
        var regional = new RegionalSettings();
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegInternational);
            if (key == null)
                return;
            regional.ShortDateFormat = key.GetValue("sShortDate") as string;
            regional.LongDateFormat = key.GetValue("sLongDate") as string;
            regional.ShortTimeFormat = key.GetValue("sShortTime") as string;
            regional.TimeFormat = key.GetValue("sTimeFormat") as string;
            regional.TimeMode = key.GetValue("iTime") as string;
            regional.DateOrder = key.GetValue("iDate") as string;
            regional.AmSymbol = key.GetValue("s1159") as string;
            regional.PmSymbol = key.GetValue("s2359") as string;
            regional.DateSeparator = key.GetValue("sDate") as string;
            regional.TimeSeparator = key.GetValue("sTime") as string;
            regional.LeadingZeroInTime = key.GetValue("iTLZero") as string;
            regional.DecimalSeparator = key.GetValue("sDecimal") as string;
            regional.ThousandSeparator = key.GetValue("sThousand") as string;
            regional.DigitGrouping = key.GetValue("sGrouping") as string;
            regional.DecimalDigits = key.GetValue("iDigits") as string;
            regional.Measurement = key.GetValue("iMeasure") as string;
            regional.CurrencySymbol = key.GetValue("sCurrency") as string;
            regional.CurrencyDecimalSep = key.GetValue("sMonDecimalSep") as string;
            regional.CurrencyThousandSep = key.GetValue("sMonThousandSep") as string;
            regional.CurrencyDigits = key.GetValue("iCurrDigits") as string;
            regional.CurrencyPositivePattern = key.GetValue("iCurrency") as string;
            regional.CurrencyNegativePattern = key.GetValue("iNegCurr") as string;
        }, "reading");
        return regional;
    }

    public static void Write(RegionalSettings regional)
    {
        bool changed = false;
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegInternational);

            void Set(string name, string? val)
            {
                if (val == null)
                    return;
                key.SetValue(name, val, RegistryValueKind.String);
                changed = true;
            }

            Set("sShortDate", regional.ShortDateFormat);
            Set("sLongDate", regional.LongDateFormat);
            Set("sShortTime", regional.ShortTimeFormat);
            Set("sTimeFormat", regional.TimeFormat);
            Set("iTime", regional.TimeMode);
            Set("iDate", regional.DateOrder);
            Set("s1159", regional.AmSymbol);
            Set("s2359", regional.PmSymbol);
            Set("sDate", regional.DateSeparator);
            Set("sTime", regional.TimeSeparator);
            Set("iTLZero", regional.LeadingZeroInTime);
            Set("sDecimal", regional.DecimalSeparator);
            Set("sThousand", regional.ThousandSeparator);
            Set("sGrouping", regional.DigitGrouping);
            Set("iDigits", regional.DecimalDigits);
            Set("iMeasure", regional.Measurement);
            Set("sCurrency", regional.CurrencySymbol);
            Set("sMonDecimalSep", regional.CurrencyDecimalSep);
            Set("sMonThousandSep", regional.CurrencyThousandSep);
            Set("iCurrDigits", regional.CurrencyDigits);
            Set("iCurrency", regional.CurrencyPositivePattern);
            Set("iNegCurr", regional.CurrencyNegativePattern);
        }, "writing");
        if (changed)
            BroadcastHelper.BroadcastIntl();
    }
}