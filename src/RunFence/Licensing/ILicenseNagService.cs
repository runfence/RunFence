namespace RunFence.Licensing;

public interface ILicenseNagService
{
    bool ShouldShowNagByCadence(DateTime now);

    void RecordNagShown(DateTime now);
}

