using RunFence.Account;
using Xunit;

namespace RunFence.Tests;

public class UsernameHelperTests
{
    [Theory]
    [InlineData("C:\\Apps\\caf\u00e9.exe", "cafe")] // Accented Latin: diacritics stripped
    [InlineData(@"C:\Apps\Программа.exe", "Programma")] // Cyrillic transliterated
    [InlineData(@"C:\Apps\Мой App 2.exe", "MoyApp2")] // Mixed Cyrillic and ASCII
    [InlineData(@"C:\Apps\app[v2].exe", "appv2")] // SAM-invalid chars stripped
    [InlineData(@"C:\Apps\my-app+v2!.exe", "myappv2")] // Special chars stripped
    [InlineData(@"C:\Apps\my_app.exe", "my_app")] // Underscore preserved
    [InlineData(@"C:\Apps\app123.exe", "app123")] // Digits preserved
    [InlineData(@"C:\Apps\Ёлка.exe", "Yolka")] // Cyrillic Yo transliterated
    [InlineData("C:\\Apps\\f\u00fcr.exe", "fur")] // German umlaut: diacritics stripped
    [InlineData(@"C:\Apps\MyApp.exe", "MyApp")] // Simple app name
    [InlineData(@"C:\Apps\My Cool App.exe", "MyCoolApp")] // Spaces stripped
    [InlineData(@"C:\Apps\myapp", "myapp")] // No extension
    public void GenerateFromPath_ProducesExpectedPrefix(string path, string expectedPrefix)
    {
        var result = UsernameHelper.GenerateFromPath(path);
        Assert.StartsWith(expectedPrefix, result);
    }

    [Fact]
    public void GenerateFromPath_TruncationAt20Chars()
    {
        var result = UsernameHelper.GenerateFromPath(@"C:\Apps\ThisIsAVeryLongApplicationName.exe");
        Assert.Equal(20, result.Length);
        // App name truncated to 10 chars + 10 char timestamp
        Assert.StartsWith("ThisIsAVer", result);
    }

    [Fact]
    public void GenerateFromPath_CustomMaxLength()
    {
        var result = UsernameHelper.GenerateFromPath(@"C:\Apps\LongNameApp.exe", maxLength: 14);
        Assert.Equal(14, result.Length); // 4 chars name + 10 timestamp
        Assert.StartsWith("Long", result);
    }

    [Fact]
    public void GenerateFromPath_CjkOnly_FallsBackToTimestamp()
    {
        var result = UsernameHelper.GenerateFromPath(@"C:\Apps\日本語.exe");
        Assert.Matches(@"^\d{10}$", result);
    }

    [Fact]
    public void GenerateFromPath_TimestampFormat_TenDigits()
    {
        var result = UsernameHelper.GenerateFromPath(@"C:\Apps\test.exe");
        // Should end with 10-digit timestamp (yyMMddHHmm)
        var timestamp = result[^10..];
        Assert.Matches(@"^\d{10}$", timestamp);
    }

    [Fact]
    public void GenerateFromPath_CyrillicExpansionTruncation()
    {
        // 6x "Щ" expands to "ShchShchShchShchShchShch" (24 chars)
        // which exceeds the 10-char name budget (maxLength 20 - timestamp 10)
        // so the name portion is truncated to 10 chars
        var result = UsernameHelper.GenerateFromPath(@"C:\ЩЩЩЩЩЩ.exe");
        Assert.Equal(20, result.Length);
        Assert.StartsWith("ShchShchSh", result);
    }
}