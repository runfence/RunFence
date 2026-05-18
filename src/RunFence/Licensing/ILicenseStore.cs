namespace RunFence.Licensing;

public interface ILicenseStore
{
    LicenseStoreResult Load();
    LicenseStoreResult Save(string key);
    LicenseStoreResult Remove();
}
