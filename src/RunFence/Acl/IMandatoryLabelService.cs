namespace RunFence.Acl;

/// <summary>
/// Low-level SACL mandatory-label operations for Low Integrity grants.
/// </summary>
public interface IMandatoryLabelService
{
    void ApplyLowIntegrityLabel(string path);
    string? ReadMandatoryLabel(string path);
    void RestoreMandatoryLabel(string path, string? previousLabel);
}
