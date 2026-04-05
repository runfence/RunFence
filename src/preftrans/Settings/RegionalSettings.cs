namespace PrefTrans.Settings;

public class RegionalSettings
{
    public string? ShortDateFormat { get; set; }
    public string? LongDateFormat { get; set; }
    public string? ShortTimeFormat { get; set; }

    public string? TimeFormat { get; set; }

    // Time/date semantic flags — root cause of 12/24h and MM/DD vs DD/MM not applying on import
    public string? TimeMode { get; set; } // iTime: "0"=12hr, "1"=24hr
    public string? DateOrder { get; set; } // iDate: "0"=MDY, "1"=DMY, "2"=YMD
    public string? AmSymbol { get; set; } // s1159
    public string? PmSymbol { get; set; } // s2359
    public string? DateSeparator { get; set; } // sDate
    public string? TimeSeparator { get; set; } // sTime

    public string? LeadingZeroInTime { get; set; } // iTLZero: "0"=no, "1"=yes

    // Number format
    public string? DecimalSeparator { get; set; } // sDecimal
    public string? ThousandSeparator { get; set; } // sThousand
    public string? DigitGrouping { get; set; } // sGrouping
    public string? DecimalDigits { get; set; } // iDigits

    public string? Measurement { get; set; } // iMeasure: "0"=metric, "1"=US

    // Currency format
    public string? CurrencySymbol { get; set; } // sCurrency
    public string? CurrencyDecimalSep { get; set; } // sMonDecimalSep
    public string? CurrencyThousandSep { get; set; } // sMonThousandSep
    public string? CurrencyDigits { get; set; } // iCurrDigits
    public string? CurrencyPositivePattern { get; set; } // iCurrency
    public string? CurrencyNegativePattern { get; set; } // iNegCurr
}