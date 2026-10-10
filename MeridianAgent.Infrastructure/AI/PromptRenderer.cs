namespace MeridianAgent.Infrastructure.AI;

public static class PromptRenderer
{
    private static readonly Regex Placeholder = new(@"\$(\w+)", RegexOptions.Compiled);

    /// <summary>
    /// Substitutes $Placeholder tokens. C# style ($Name) is used instead of {Name} because the agent
    /// prompts contain literal JSON braces.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        return Placeholder.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            return values.TryGetValue(name, out var value)
                ? value
                : throw new KeyNotFoundException($"Missing value for placeholder '{match.Value}'.");
        });
    }
}

/// <summary>Tolerant parsing helpers for the (sometimes sloppy) JSON small language models emit.</summary>
public static class EnumParse
{
    public static bool TryParseEnum<T>(string? raw, out T value)
        where T : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = new string(raw.Trim().Where(char.IsLetter).ToArray());
        return normalized.Length > 0
            && Enum.TryParse<T>(normalized, ignoreCase: true, out value)
            && Enum.IsDefined(value);
    }
}

/// <summary>Result of one agent attempt: a parsed value, or the validation errors explaining the retry.</summary>
public sealed record ParseOutcome<T>(T? Value, IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Errors.Count == 0;

    public static ParseOutcome<T> Ok(T value) => new(value, Array.Empty<string>());

    public static ParseOutcome<T> Fail(params string[] errors) => new(default, errors);
}

public static class JsonElementReader
{
    public static string? ReadString(JsonElement element, string name, List<string> errors, bool required)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            if (required)
            {
                errors.Add($"missing required field '{name}'");
            }

            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                errors.Add($"field '{name}' is required");
            }

            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            errors.Add($"field '{name}' must be a string");
            return null;
        }

        var value = property.GetString();
        if (required && string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"field '{name}' must not be empty");
        }

        return value;
    }

    public static int ReadInt(JsonElement element, string name, List<string> errors, int fallback = 0)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        errors.Add($"field '{name}' must be a number");
        return fallback;
    }

    public static T? ReadEnum<T>(JsonElement element, string name, List<string> errors, bool required)
        where T : struct, Enum
    {
        var raw = ReadString(element, name, errors, required: false);
        if (raw is null)
        {
            if (required)
            {
                errors.Add($"field '{name}' is required (allowed: {Allowed<T>()})");
            }

            return null;
        }

        if (EnumParse.TryParseEnum<T>(raw, out var value))
        {
            return value;
        }

        errors.Add($"field '{name}' has invalid value '{raw}' (allowed: {Allowed<T>()})");
        return null;
    }

    public static IReadOnlyList<string>? ReadStringList(JsonElement element, string name, List<string> errors)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"field '{name}' must be an array of strings");
            return null;
        }

        var items = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                items.Add(value);
            }
        }

        return items.Count == 0 ? null : items;
    }

    public static string? ReadProfileString(JsonElement profile, params string[] names)
    {
        if (profile.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (profile.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonDefaults.Options);

    public static string ToInvariant(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    public static string ToJsonLiteral(bool value) => value ? "true" : "false";

    private static string Allowed<T>() where T : struct, Enum => string.Join(" | ", Enum.GetNames<T>());
}
