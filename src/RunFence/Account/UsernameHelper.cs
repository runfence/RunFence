using System.Globalization;
using System.Text;

namespace RunFence.Account;

public static class UsernameHelper
{
    /// <summary>
    /// Generates a SAM-compatible username from a file path.
    /// Extracts the file name (no extension), transliterates Cyrillic,
    /// strips diacritics, keeps ASCII letters/digits/underscore, truncates to maxLength.
    /// Falls back to a timestamp-based name if the result is empty.
    /// </summary>
    public static string GenerateFromPath(string filePath, int maxLength = 20)
    {
        var timestamp = DateTime.Now.ToString("yyMMddHHmm");
        var nameMaxLength = maxLength - timestamp.Length;
        if (nameMaxLength <= 0)
            return timestamp;

        var baseName = Path.GetFileNameWithoutExtension(filePath);

        var sb = new StringBuilder(baseName.Length);
        foreach (var ch in baseName)
        {
            if (CyrillicMap.TryGetValue(ch, out var latin))
                sb.Append(latin);
            else
                sb.Append(ch);
        }

        // Normalize to FormD (decomposed) to separate base chars from combining marks
        var normalized = sb.ToString().Normalize(NormalizationForm.FormD);

        var result = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue; // strip combining diacritics

            if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
            {
                result.Append(ch);
            }
            // All other characters (spaces, specials, SAM-invalid) are dropped
        }

        var appName = result.Length > nameMaxLength
            ? result.ToString(0, nameMaxLength)
            : result.ToString();

        return appName.Length > 0
            ? $"{appName}{timestamp}"
            : timestamp;
    }

    private static readonly Dictionary<char, string> CyrillicMap = new()
    {
        // Uppercase
        { '\u0410', "A" }, // А
        { '\u0411', "B" }, // Б
        { '\u0412', "V" }, // В
        { '\u0413', "G" }, // Г
        { '\u0414', "D" }, // Д
        { '\u0415', "E" }, // Е
        { '\u0416', "Zh" }, // Ж
        { '\u0417', "Z" }, // З
        { '\u0418', "I" }, // И
        { '\u0419', "Y" }, // Й
        { '\u041A', "K" }, // К
        { '\u041B', "L" }, // Л
        { '\u041C', "M" }, // М
        { '\u041D', "N" }, // Н
        { '\u041E', "O" }, // О
        { '\u041F', "P" }, // П
        { '\u0420', "R" }, // Р
        { '\u0421', "S" }, // С
        { '\u0422', "T" }, // Т
        { '\u0423', "U" }, // У
        { '\u0424', "F" }, // Ф
        { '\u0425', "Kh" }, // Х
        { '\u0426', "Ts" }, // Ц
        { '\u0427', "Ch" }, // Ч
        { '\u0428', "Sh" }, // Ш
        { '\u0429', "Shch" }, // Щ
        { '\u042A', "" }, // Ъ (hard sign)
        { '\u042B', "Y" }, // Ы
        { '\u042C', "" }, // Ь (soft sign)
        { '\u042D', "E" }, // Э
        { '\u042E', "Yu" }, // Ю
        { '\u042F', "Ya" }, // Я

        // Lowercase
        { '\u0430', "a" }, // а
        { '\u0431', "b" }, // б
        { '\u0432', "v" }, // в
        { '\u0433', "g" }, // г
        { '\u0434', "d" }, // д
        { '\u0435', "e" }, // е
        { '\u0436', "zh" }, // ж
        { '\u0437', "z" }, // з
        { '\u0438', "i" }, // и
        { '\u0439', "y" }, // й
        { '\u043A', "k" }, // к
        { '\u043B', "l" }, // л
        { '\u043C', "m" }, // м
        { '\u043D', "n" }, // н
        { '\u043E', "o" }, // о
        { '\u043F', "p" }, // п
        { '\u0440', "r" }, // р
        { '\u0441', "s" }, // с
        { '\u0442', "t" }, // т
        { '\u0443', "u" }, // у
        { '\u0444', "f" }, // ф
        { '\u0445', "kh" }, // х
        { '\u0446', "ts" }, // ц
        { '\u0447', "ch" }, // ч
        { '\u0448', "sh" }, // ш
        { '\u0449', "shch" }, // щ
        { '\u044A', "" }, // ъ (hard sign)
        { '\u044B', "y" }, // ы
        { '\u044C', "" }, // ь (soft sign)
        { '\u044D', "e" }, // э
        { '\u044E', "yu" }, // ю
        { '\u044F', "ya" }, // я

        // Common extras
        { '\u0401', "Yo" }, // Ё
        { '\u0451', "yo" }, // ё
    };
}