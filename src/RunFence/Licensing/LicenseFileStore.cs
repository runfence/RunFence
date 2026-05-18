using RunFence.Core;

namespace RunFence.Licensing;

public class LicenseFileStore : ILicenseStore
{
    private readonly string _licenseFilePath;

    public LicenseFileStore() : this(PathConstants.LicenseFilePath)
    {
    }

    public LicenseFileStore(string licenseFilePath)
    {
        _licenseFilePath = licenseFilePath;
    }

    public LicenseStoreResult Load()
    {
        try
        {
            if (!File.Exists(_licenseFilePath))
                return new LicenseStoreResult(LicenseStoreStatus.NotFound, null, null);

            var key = File.ReadAllText(_licenseFilePath).Trim();
            if (string.IsNullOrEmpty(key))
                return new LicenseStoreResult(LicenseStoreStatus.CorruptData, null, "License file is empty.");

            return new LicenseStoreResult(LicenseStoreStatus.Succeeded, key, null);
        }
        catch (Exception ex)
        {
            return new LicenseStoreResult(LicenseStoreStatus.Failed, null, ex.Message);
        }
    }

    public LicenseStoreResult Save(string key)
    {
        string? tmp = null;
        try
        {
            var dir = Path.GetDirectoryName(_licenseFilePath)!;
            Directory.CreateDirectory(dir);
            tmp = _licenseFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, key);
            if (File.Exists(_licenseFilePath))
                File.Replace(tmp, _licenseFilePath, _licenseFilePath + ".bak");
            else
                File.Move(tmp, _licenseFilePath);
            return new LicenseStoreResult(LicenseStoreStatus.Succeeded, key, null);
        }
        catch (Exception ex)
        {
            return new LicenseStoreResult(LicenseStoreStatus.PersistenceFailed, null, ex.Message);
        }
        finally
        {
            if (!string.IsNullOrEmpty(tmp) && File.Exists(tmp))
            {
                try { File.Delete(tmp); } catch { }
            }
        }
    }

    public LicenseStoreResult Remove()
    {
        try
        {
            if (File.Exists(_licenseFilePath))
                File.Delete(_licenseFilePath);
            return new LicenseStoreResult(LicenseStoreStatus.Succeeded, null, null);
        }
        catch (Exception ex)
        {
            return new LicenseStoreResult(LicenseStoreStatus.PersistenceFailed, null, ex.Message);
        }
    }
}
