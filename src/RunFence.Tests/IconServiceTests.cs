using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class IconServiceTests : IDisposable
{
    private readonly IconService _service;
    private readonly TempDirectory _tempDir;
    private readonly string _iconDir;

    public IconServiceTests()
    {
        var log = new Mock<ILoggingService>();
        _tempDir = new TempDirectory("RunFence_IconTest");
        _iconDir = Path.Combine(_tempDir.Path, "icons");
        Directory.CreateDirectory(_iconDir);
        _service = new IconService(log.Object, iconDir: _iconDir);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public void NeedsRegeneration_UrlScheme_ReturnsFalse()
    {
        var app = new AppEntry { IsUrlScheme = true, ExePath = "steam://run/123" };
        Assert.False(_service.NeedsRegeneration(app));
    }

    [Fact]
    public void NeedsRegeneration_NoIconFile_ReturnsTrue()
    {
        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            IsUrlScheme = false,
            ExePath = @"C:\nonexistent\app.exe"
        };
        Assert.True(_service.NeedsRegeneration(app));
    }

    [Fact]
    public void NeedsRegeneration_NullTimestamp_ReturnsTrue()
    {
        var appId = AppEntry.GenerateId();
        File.WriteAllBytes(Path.Combine(_iconDir, $"{appId}.ico"), Array.Empty<byte>());

        var exePath = Path.Combine(_tempDir.Path, "testapp.exe");
        File.WriteAllBytes(exePath, Array.Empty<byte>());

        var app = new AppEntry
        {
            Id = appId,
            IsUrlScheme = false,
            ExePath = exePath,
            LastKnownExeTimestamp = null
        };
        Assert.True(_service.NeedsRegeneration(app));
    }

    [Fact]
    public void NeedsRegeneration_TimestampMismatch_ReturnsTrue()
    {
        var exePath = Path.Combine(_tempDir.Path, "testapp.exe");
        File.WriteAllBytes(exePath, Array.Empty<byte>());

        var appId = AppEntry.GenerateId();
        File.WriteAllBytes(Path.Combine(_iconDir, $"{appId}.ico"), Array.Empty<byte>());

        var app = new AppEntry
        {
            Id = appId,
            IsUrlScheme = false,
            ExePath = exePath,
            LastKnownExeTimestamp = DateTime.UtcNow.AddHours(-1) // Stale timestamp
        };

        Assert.True(_service.NeedsRegeneration(app));
    }

    [Fact]
    public void NeedsRegeneration_TimestampMatches_ReturnsFalse()
    {
        var exePath = Path.Combine(_tempDir.Path, "testapp.exe");
        File.WriteAllBytes(exePath, Array.Empty<byte>());
        var exeTimestamp = File.GetLastWriteTimeUtc(exePath);

        var appId = AppEntry.GenerateId();
        File.WriteAllBytes(Path.Combine(_iconDir, $"{appId}.ico"), Array.Empty<byte>());

        var app = new AppEntry
        {
            Id = appId,
            IsUrlScheme = false,
            ExePath = exePath,
            LastKnownExeTimestamp = exeTimestamp // Matches
        };

        Assert.False(_service.NeedsRegeneration(app));
    }

    [Fact]
    public void NeedsRegeneration_ExeDoesNotExist_ButIconExists_ReturnsFalse()
    {
        var appId = AppEntry.GenerateId();
        File.WriteAllBytes(Path.Combine(_iconDir, $"{appId}.ico"), Array.Empty<byte>());

        var app = new AppEntry
        {
            Id = appId,
            IsUrlScheme = false,
            ExePath = Path.Combine(_tempDir.Path, "nonexistent.exe"),
            LastKnownExeTimestamp = DateTime.UtcNow
        };

        // Icon exists, exe doesn't → returns false (can't regenerate without exe)
        Assert.False(_service.NeedsRegeneration(app));
    }

    [Fact]
    public void DeleteIcon_RemovesExistingFile()
    {
        var appId = AppEntry.GenerateId();
        var iconPath = Path.Combine(_iconDir, $"{appId}.ico");
        File.WriteAllBytes(iconPath, Array.Empty<byte>());

        _service.DeleteIcon(appId);

        Assert.False(File.Exists(iconPath));
    }

    [Fact]
    public void DeleteIcon_NonExistentFile_DoesNotThrow()
    {
        _service.DeleteIcon(AppEntry.GenerateId());
    }
}