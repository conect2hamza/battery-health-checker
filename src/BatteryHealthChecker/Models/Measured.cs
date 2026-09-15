namespace BatteryHealthChecker.Models;

/// <summary>
/// Where a reading came from. Ordered weakest-to-strongest so that merging can
/// prefer the more authoritative source (SRS 4: multiple data sources with a
/// clean fallback abstraction).
/// </summary>
public enum DataSource
{
    None = 0,
    SystemPowerStatus = 1,
    Win32Battery = 2,
    WmiAcpi = 3,
    BatteryIoctl = 4,
    Calculated = 5,
}

/// <summary>
/// A single hardware reading that is either genuinely available or genuinely not.
///
/// SRS 2 / 20: the application must never fabricate unavailable information and
/// must never show a missing value as 0. Making "unavailable" a first-class state
/// of every reading is what enforces that at the type level - a consumer cannot
/// reach <see cref="Value"/> by accident without going through
/// <see cref="IsAvailable"/> or one of the formatting helpers.
/// </summary>
/// <typeparam name="T">The underlying value type.</typeparam>
public readonly struct Measured<T> where T : struct
{
    private readonly T _value;

    private Measured(T value, bool isAvailable, DataSource source, string? note)
    {
        _value = value;
        IsAvailable = isAvailable;
        Source = source;
        Note = note;
    }

    /// <summary>True when the hardware or Windows actually reported this value.</summary>
    public bool IsAvailable { get; }

    /// <summary>Which collector produced the value. <see cref="DataSource.None"/> when unavailable.</summary>
    public DataSource Source { get; }

    /// <summary>
    /// Optional qualifier, e.g. why a value is missing or why it is considered suspicious.
    /// Surfaced in reports and details, never used to invent a number.
    /// </summary>
    public string? Note { get; }

    /// <summary>The reading. Only meaningful when <see cref="IsAvailable"/> is true.</summary>
    public T Value => _value;

    /// <summary>The reading, or null when unavailable.</summary>
    public T? ValueOrNull => IsAvailable ? _value : null;

    public static Measured<T> Unavailable(string? note = null) => new(default, false, DataSource.None, note);

    public static Measured<T> From(T value, DataSource source, string? note = null) =>
        new(value, true, source, note);

    /// <summary>Creates a reading from a nullable, collapsing null to <see cref="Unavailable"/>.</summary>
    public static Measured<T> FromNullable(T? value, DataSource source, string? note = null) =>
        value.HasValue ? From(value.Value, source, note) : Unavailable(note);

    /// <summary>
    /// Returns whichever reading is available; when both are, the one from the more
    /// authoritative source wins. This is the merge rule for the collector chain.
    /// </summary>
    public Measured<T> Or(Measured<T> fallback)
    {
        if (!IsAvailable) return fallback;
        if (!fallback.IsAvailable) return this;
        return fallback.Source > Source ? fallback : this;
    }

    /// <summary>Drops the reading when it fails a sanity check (SRS 20: data validation).</summary>
    public Measured<T> Where(Func<T, bool> predicate, string? rejectionNote = null) =>
        IsAvailable && !predicate(_value) ? Unavailable(rejectionNote) : this;

    /// <summary>Projects an available reading, preserving source and availability.</summary>
    public Measured<TOut> Select<TOut>(Func<T, TOut> selector) where TOut : struct =>
        IsAvailable ? Measured<TOut>.From(selector(_value), Source, Note) : Measured<TOut>.Unavailable(Note);

    /// <summary>Attaches an explanatory note without changing the reading.</summary>
    public Measured<T> WithNote(string? note) => new(_value, IsAvailable, Source, note);

    public override string ToString() => IsAvailable ? _value.ToString() ?? string.Empty : Strings.NotAvailable;
}

/// <summary>Shared user-facing literals so the "Not Available" wording stays consistent (SRS 2, 10, 19).</summary>
public static class Strings
{
    public const string NotAvailable = "Not Available";
    public const string Calculating = "Calculating...";
}
