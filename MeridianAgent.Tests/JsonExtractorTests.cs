namespace MeridianAgent.Tests;

public class JsonExtractorTests
{
    [Fact]
    public void TryExtract_ParsesPlainObject()
    {
        var found = JsonExtractor.TryExtract("""{"status": "CLEARED", "warnings": []}""", out var document);

        Assert.True(found);
        using var _ = document;
        Assert.Equal("CLEARED", document!.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void TryExtract_StripsMarkdownFences()
    {
        const string fenced = """
            Sure, here is the plan:
            ```json
            [{"venue_name": "Karak House"}]
            ```
            Hope that helps!
            """;

        var found = JsonExtractor.TryExtract(fenced, out var document);

        Assert.True(found);
        using var _ = document;
        Assert.Equal(JsonValueKind.Array, document!.RootElement.ValueKind);
    }

    [Fact]
    public void TryExtract_AnchorsOnFirstTopLevelOpener_IgnoringInnerArrays()
    {
        const string truncated = """
            {"venue_name": "Gion Night Walk", "tags": ["a", "b"], "best_time_
            """;

        var found = JsonExtractor.TryExtract(truncated, out var document);

        Assert.False(found);
        Assert.Null(document);
    }

    [Fact]
    public void TryExtract_TakesFirstCompleteValueAfterProse()
    {
        const string noisy = "Let me think... Here you go:\n{\"query_intent\": \"food\"}\nDone!";

        var found = JsonExtractor.TryExtract(noisy, out var document);

        Assert.True(found);
        using var _ = document;
        Assert.Equal("food", document!.RootElement.GetProperty("query_intent").GetString());
    }

    [Fact]
    public void TryExtract_RepairsTrailingCommas()
    {
        var found = JsonExtractor.TryExtract("""{"a": 1, "b": [1, 2,],}""", out var document);

        Assert.True(found);
        using var _ = document;
    }

    [Fact]
    public void TryExtract_ReturnsFalseForEmptyText()
    {
        Assert.False(JsonExtractor.TryExtract("   ", out var document));
        Assert.Null(document);
    }

    [Fact]
    public void TryGetErrorMessage_DetectsErrorObjects()
    {
        using var document = JsonDocument.Parse("""{"error": true, "reason": "nothing fits"}""");

        Assert.True(JsonExtractor.TryGetErrorMessage(document.RootElement, out var reason));
        Assert.Equal("nothing fits", reason);
    }

    [Fact]
    public async Task ExtractWithLlmAsync_FallsBackToSanitizerModel()
    {
        var client = new ScriptedLlmClient(
            """Here is the JSON: {"status": "FLAGGED", "warnings": []}""");

        var element = await JsonExtractor.ExtractWithLlmAsync(client, "qwen2.5:1.5b", "not json at all: true");

        Assert.Equal("FLAGGED", element.GetProperty("status").GetString());
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task ExtractWithLlmAsync_DoesNotSanitizeWhenDirectParseWorks()
    {
        var client = new ScriptedLlmClient("""{"status": "CLEARED"}""");

        var element = await JsonExtractor.ExtractWithLlmAsync(client, "qwen2.5:1.5b", "all good");

        Assert.Equal("CLEARED", element.GetProperty("status").GetString());
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task ExtractWithLlmAsync_ThrowsOnErrorObject()
    {
        var client = new ScriptedLlmClient("""{"error": true, "reason": "no venue fits"}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JsonExtractor.ExtractWithLlmAsync(client, "qwen2.5:1.5b", "whatever"));
    }

    [Fact]
    public async Task ExtractWithLlmAsync_ReturnsErrorObjectWhenAllowed()
    {
        var client = new ScriptedLlmClient("""{"error": true, "reason": "no venue fits"}""");

        var element = await JsonExtractor.ExtractWithLlmAsync(
            client,
            "qwen2.5:1.5b",
            "whatever",
            allowErrorObject: true);

        Assert.True(element.GetProperty("error").GetBoolean());
    }

    [Fact]
    public async Task ExtractWithLlmAsync_ThrowsWhenSanitizerAlsoFails()
    {
        var client = new ScriptedLlmClient(
            "partial *(& broken",
            "still not json");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JsonExtractor.ExtractWithLlmAsync(client, "qwen2.5:1.5b", "total garbage"));
    }
}

public class PromptRendererTests
{
    [Fact]
    public void Render_ReplacesPlaceholders()
    {
        var rendered = PromptRenderer.Render(
            "Hello $Name, it is $Weather in $City.",
            new Dictionary<string, string>
            {
                ["Name"] = "Aria",
                ["Weather"] = "raining",
                ["City"] = "Dubai",
            });

        Assert.Equal("Hello Aria, it is raining in Dubai.", rendered);
    }

    [Fact]
    public void Render_ThrowsOnMissingPlaceholder()
    {
        Assert.Throws<KeyNotFoundException>(() =>
            PromptRenderer.Render("Missing $Value here.", new Dictionary<string, string>()));
    }

    [Fact]
    public void Render_LeavesJsonBracesAlone()
    {
        var template = """{"location": "$Location", "meta": {"tz": 9}}""";

        var rendered = PromptRenderer.Render(template, new Dictionary<string, string> { ["Location"] = "Dubai" });

        Assert.Equal("""{"location": "Dubai", "meta": {"tz": 9}}""", rendered);
    }
}
