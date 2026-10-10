namespace MeridianAgent.Infrastructure.AI;

/// <summary>Shared JSON serialization options: snake_case everywhere, tolerant enum strings.</summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
}

/// <summary>
/// Extracts the first valid top-level JSON value out of an SLM response.
/// SLMs habitually wrap JSON in markdown fences, add prose, or leave trailing commas - all repaired here.
/// When local extraction fails, a JSON-sanitizer model is asked to repair the output.
/// </summary>
public static class JsonExtractor
{
    private static readonly Regex TrailingComma = new(@",\s*(?=[}\]])", RegexOptions.Compiled);
    private static readonly Regex CodeFence = new(@"```(?:json|JSON)?\s*|\s*```", RegexOptions.Compiled);

    public const string SanitizerPrompt = """
        You are a strict JSON repair tool. The text below came from another language model that was supposed to output ONLY a JSON value.

        <raw_output>
        $RawAgentOutput
        </raw_output>

        STRICT RULES:
        1. Output ONLY the repaired JSON value - no markdown, no explanations.
        2. Keep the exact same structure and fields. Never invent, rename, or drop fields.
        3. Remove prose, code fences, and leading/trailing commentary around the JSON.
        4. Remove trailing commas before } or ].
        5. If the text contains no JSON value at all, output exactly: {"error": true, "reason": "no JSON found"}
        6. Never wrap your answer in markdown code fences.
        """;

    public static bool TryExtract(string? text, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = CodeFence.Replace(text, string.Empty);
        if (TryParse(cleaned, out document))
        {
            return true;
        }

        var start = -1;
        for (var index = 0; index < cleaned.Length; index++)
        {
            if (cleaned[index] is '{' or '[')
            {
                start = index;
                break;
            }
        }

        if (start < 0)
        {
            return false;
        }

        var end = FindBalancedEnd(cleaned, start);
        if (end < 0)
        {
            return false;
        }

        var candidate = cleaned[start..(end + 1)];
        if (TryParse(candidate, out document))
        {
            return true;
        }

        if (TryParse(TrailingComma.Replace(candidate, string.Empty), out document))
        {
            return true;
        }

        if (TryParse(TrailingComma.Replace(cleaned, string.Empty), out document))
        {
            return true;
        }

        document = null;
        return false;
    }

    public static bool TryGetErrorMessage(JsonElement element, out string? reason)
    {
        reason = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!element.TryGetProperty("error", out var errorProperty))
        {
            return false;
        }

        var isError = errorProperty.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(errorProperty.GetString(), out var parsed) && parsed,
            _ => false,
        };

        if (!isError)
        {
            return false;
        }

        if (element.TryGetProperty("reason", out var reasonProperty) && reasonProperty.ValueKind == JsonValueKind.String)
        {
            reason = reasonProperty.GetString();
        }

        return true;
    }

    /// <summary>
    /// Extracts JSON from <paramref name="raw"/>, falling back to the sanitizer model when local repair fails.
    /// When <paramref name="allowErrorObject"/> is true, an {"error": true, "reason": ...} payload is returned as-is
    /// so the calling agent can react to it.
    /// </summary>
    public static async Task<JsonElement> ExtractWithLlmAsync(
        ILlmClient client,
        string sanitizerModel,
        string raw,
        CancellationToken cancellationToken = default,
        bool allowErrorObject = false)
    {
        if (TryExtract(raw, out var document))
        {
            using (document)
            {
                if (TryGetErrorMessage(document!.RootElement, out var embeddedReason))
                {
                    if (allowErrorObject)
                    {
                        return document.RootElement.Clone();
                    }

                    throw new InvalidOperationException($"The model reported an error: {embeddedReason ?? "unknown"}");
                }

                return document.RootElement.Clone();
            }
        }

        var sanitizerPrompt = PromptRenderer.Render(
            SanitizerPrompt,
            new Dictionary<string, string> { ["RawAgentOutput"] = raw });

        var cleaned = await client
            .CompleteAsync(new LlmRequest(sanitizerModel, sanitizerPrompt, JsonMode: true), cancellationToken)
            .ConfigureAwait(false);

        if (TryExtract(cleaned, out var repaired))
        {
            using (repaired)
            {
                if (TryGetErrorMessage(repaired!.RootElement, out var unparseableReason))
                {
                    if (allowErrorObject)
                    {
                        return repaired.RootElement.Clone();
                    }

                    throw new InvalidOperationException(
                        $"The JSON sanitizer could not extract valid JSON: {unparseableReason ?? "unparseable"}");
                }

                return repaired.RootElement.Clone();
            }
        }

        throw new InvalidOperationException("Could not extract valid JSON from the model response.");
    }

    private static bool TryParse(string text, out JsonDocument? document)
    {
        document = null;
        try
        {
            document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int FindBalancedEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character is '{' or '[')
            {
                depth++;
            }
            else if (character is '}' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }
}
