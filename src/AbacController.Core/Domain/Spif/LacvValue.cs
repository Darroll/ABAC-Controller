namespace AbacController.Core.Domain.Spif;

/// <summary>
/// Label and Certificate Value — the numeric code stored in security labels.
/// Wraps an integer to provide type safety and avoid accidental misuse.
/// </summary>
public readonly record struct LacvValue(int Value) : IComparable<LacvValue>
{
    public int CompareTo(LacvValue other) => Value.CompareTo(other.Value);

    public static implicit operator int(LacvValue v) => v.Value;
    public static implicit operator LacvValue(int v) => new(v);

    public override string ToString() => Value.ToString();
}
