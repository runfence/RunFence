namespace RunFence.Licensing;

public class LicenseNagService(ILicenseNagStore nagStore) : ILicenseNagService
{
    private readonly ILicenseNagStore _licenseNagStore = nagStore;

    public bool ShouldShowNagByCadence(DateTime now)
    {
        var lastShown = _licenseNagStore.ReadLastNagDate();
        if (lastShown == null)
            return true;

        // Forward time protection: future datetime -> treat as invalid -> show nag.
        if (lastShown.Value > now)
            return true;

        return now - lastShown.Value >= TimeSpan.FromHours(24);
    }

    public void RecordNagShown(DateTime now)
    {
        try
        {
            _licenseNagStore.WriteLastNagDate(now);
        }
        catch
        {
        }
    }
}
