using static System.StringComparison;

namespace RunFence.Core.Models;

public record AllowAclEntry
{
    public string Sid { get; init; } = string.Empty;
    public bool AllowExecute { get; set; }
    public bool AllowWrite { get; set; }

    public virtual bool Equals(AllowAclEntry? other) =>
        other is not null
        && string.Equals(Sid, other.Sid, OrdinalIgnoreCase)
        && AllowExecute == other.AllowExecute
        && AllowWrite == other.AllowWrite;

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Sid), AllowExecute, AllowWrite);
}