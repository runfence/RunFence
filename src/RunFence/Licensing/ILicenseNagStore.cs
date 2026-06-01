namespace RunFence.Licensing;

public interface ILicenseNagStore
{
    DateTime? ReadLastNagDate();

    void WriteLastNagDate(DateTime shownAt);
}

