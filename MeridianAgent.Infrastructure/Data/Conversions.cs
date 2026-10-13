namespace MeridianAgent.Infrastructure.Data;

using System.Text.Json;

using MeridianAgent.Domain;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>
/// EF Core value converters that let the existing <see cref="UserProfile" /> record — with its
/// string lists and domain enums — live in a single SQLite table unchanged. Converter bodies are
/// expression trees, so the real work happens in the small static helpers below.
/// </summary>
public static class Conversions
{
    private static readonly JsonSerializerOptions ListOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>String lists become JSON arrays (values contain commas and ampersands).</summary>
    public static ValueConverter<IReadOnlyList<string>, string> StringList { get; } = new(
        value => WriteList(value),
        value => ParseList(value));

    /// <summary>
    /// Tells EF how to compare two converted lists for change detection. Without it, saving a
    /// profile whose list did not change still rewrites the column.
    /// </summary>
    public static ValueComparer<IReadOnlyList<string>> StringListComparer { get; } = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        value => value == null ? 0 : value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        value => value == null ? Array.Empty<string>() : value.ToArray());

    /// <summary>Enums are stored by name so the database stays readable without a lookup table.</summary>
    public static ValueConverter<TEnum, string> EnumByName<TEnum>() where TEnum : struct, Enum => new(
        value => value.ToString(),
        value => ParseEnum<TEnum>(value));

    /// <summary>
    /// A null list is written as an empty JSON array, never as the four characters "null": the cell
    /// stays valid JSON that SQL can read (`json_array_length`), and a reader that does not use the
    /// converter still sees an empty list rather than a deserialization surprise.
    /// </summary>
    private static string WriteList(IReadOnlyList<string>? value) =>
        JsonSerializer.Serialize(value ?? Array.Empty<string>(), ListOptions);

    private static IReadOnlyList<string> ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        try
        {
            // "null" is what the converter used to write for a null list, so it is read back as the
            // empty list it meant rather than rejected into the corrupt-cell path.
            return JsonSerializer.Deserialize<List<string>>(value, ListOptions) ?? new List<string>();
        }
        catch (JsonException)
        {
            // A corrupt cell must not take the whole profile down.
            return Array.Empty<string>();
        }
    }

    private static TEnum ParseEnum<TEnum>(string value) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : default;
}
