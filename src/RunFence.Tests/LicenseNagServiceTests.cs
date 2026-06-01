using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class LicenseNagServiceTests
{
    [Fact]
    public void ShouldShowNagByCadence_WhenNoTimestamp_IsTrue()
    {
        var service = CreateService();
        Assert.True(service.ShouldShowNagByCadence(new DateTime(2026, 5, 24, 10, 0, 0)));
    }

    [Fact]
    public void ShouldShowNagByCadence_WhenTimestampInvalid_IsTrue()
    {
        var store = new InMemoryLicenseNagStore();
        store.SetRawTimestamp("not-a-timestamp");
        var service = new LicenseNagService(store);
        Assert.True(service.ShouldShowNagByCadence(new DateTime(2026, 5, 24, 10, 0, 0)));
    }

    [Fact]
    public void ShouldShowNagByCadence_WhenTimestampInFuture_IsTrue()
    {
        var store = new InMemoryLicenseNagStore();
        var service = new LicenseNagService(store);
        service.RecordNagShown(new DateTime(2026, 5, 24, 10, 0, 0));
        Assert.True(service.ShouldShowNagByCadence(new DateTime(2026, 5, 23, 10, 0, 0)));
    }

    [Fact]
    public void ShouldShowNagByCadence_WhenTimestampExpired_IsTrue()
    {
        var store = new InMemoryLicenseNagStore();
        store.WriteLastNagDate(new DateTime(2026, 5, 23, 8, 0, 0));
        var service = new LicenseNagService(store);
        Assert.True(service.ShouldShowNagByCadence(new DateTime(2026, 5, 24, 10, 0, 0)));
    }

    [Fact]
    public void ShouldShowNagByCadence_WhenTimestampFresh_IsFalse()
    {
        var store = new InMemoryLicenseNagStore();
        store.WriteLastNagDate(new DateTime(2026, 5, 24, 9, 0, 0));
        var service = new LicenseNagService(store);
        Assert.False(service.ShouldShowNagByCadence(new DateTime(2026, 5, 24, 10, 0, 0)));
    }

    private static LicenseNagService CreateService() =>
        new(new InMemoryLicenseNagStore());

    private sealed class InMemoryLicenseNagStore : ILicenseNagStore
    {
        private string? _rawTimestamp;

        public DateTime? ReadLastNagDate()
        {
            if (_rawTimestamp == null)
                return null;

            return DateTime.TryParse(_rawTimestamp, out var date) ? date : null;
        }

        public void WriteLastNagDate(DateTime shownAt)
        {
            _rawTimestamp = shownAt.ToString("o");
        }

        public void SetRawTimestamp(string rawTimestamp) => _rawTimestamp = rawTimestamp;
    }
}

