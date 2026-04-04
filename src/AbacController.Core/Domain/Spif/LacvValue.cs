namespace AbacController.Core.Domain.Spif;

/// <summary>
/// Label and Certificate Value — the numeric code stored in security labels.
/// Wraps an integer to provide type safety and avoid accidental misuse.
/// </summary>
public readonly record struct LacvValue(int Value) : IComparable<LacvValue>
{
    /// <inheritdoc />
    public int CompareTo(LacvValue other) => Value.CompareTo(other.Value);

    /// <summary>Implicit conversion from <see cref="LacvValue"/> to <see cref="int"/>.</summary>
    public static implicit operator int(LacvValue v) => v.Value;

    /// <summary>Implicit conversion from <see cref="int"/> to <see cref="LacvValue"/>.</summary>
    public static implicit operator LacvValue(int v) => new(v);

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
