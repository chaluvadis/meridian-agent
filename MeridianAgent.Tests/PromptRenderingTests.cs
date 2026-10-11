namespace MeridianAgent.Tests;

using MeridianAgent.Domain.AgentContracts;

using MeridianAgent.Infrastructure.AI;
using MeridianAgent.Infrastructure.Data;

public class PromptRenderingTests
{
    [Fact]
    public void Render_SubstitutesEveryProvidedPlaceholder()
    {
        var rendered = PromptRenderer.Render(
            "Destination: $Destination. Traveler: $Name feels $Mood.",
            new Dictionary<string, string>
            {
                ["Destination"] = "Dubai, UAE",
                ["Name"] = "Aria",
                ["Mood"] = "tired",
            });

        Assert.Equal("Destination: Dubai, UAE. Traveler: Aria feels tired.", rendered);
    }

    [Fact]
    public void Render_PointsAtTheNameOfAnythingMissing()
    {
        var error = Assert.Throws<KeyNotFoundException>(
            () => PromptRenderer.Render("Hello $Name", new Dictionary<string, string>()));

        Assert.Contains("$Name", error.Message);
    }

    [Fact]
    public void TryParseEnum_ToleratesNoiseAroundTheValue()
    {
        Assert.True(EnumParse.TryParseEnum<VenueType>("  INDOOR  ", out var indoor));
        Assert.Equal(VenueType.Indoor, indoor);

        Assert.True(EnumParse.TryParseEnum<VenueType>("Out Door!", out var outdoor));
        Assert.Equal(VenueType.Outdoor, outdoor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("not a venue type")]
    public void TryParseEnum_RefusesAnythingItCannotName(string? raw)
    {
        Assert.False(EnumParse.TryParseEnum<VenueType>(raw, out _));
    }

    [Fact]
    public void EnumParse_RejectsAValueThatIsNotDefined()
    {
        Assert.False(EnumParse.TryParseEnum<VenueType>("Teleport", out _));
    }

    [Fact]
    public void JsonElementReader_ReportsEveryMissingRequiredField()
    {
        var errors = new List<string>();

        using var document = JsonDocument.Parse("""{"query_intent":"food"}""");

        var intent = JsonElementReader.ReadEnum<QueryIntent>(document.RootElement, "query_intent", errors, required: false);
        var name = JsonElementReader.ReadString(document.RootElement, "traveler_name", errors, required: true);
        var servings = JsonElementReader.ReadInt(document.RootElement, "group_size", errors);

        Assert.Equal(QueryIntent.Food, intent);
        Assert.Null(name);
        Assert.Equal(0, servings);
        Assert.Contains("missing required field 'traveler_name'", errors);
    }

    [Fact]
    public void JsonElementReader_NamesTheAllowedValuesWhenAnEnumIsWrong()
    {
        var errors = new List<string>();

        using var document = JsonDocument.Parse("""{"query_intent":"lunch"}""");

        var intent = JsonElementReader.ReadEnum<QueryIntent>(document.RootElement, "query_intent", errors, required: false);

        Assert.Null(intent);
        Assert.Equal(
            "field 'query_intent' has invalid value 'lunch' (allowed: Food | Sightseeing | Transport | Shopping | Nightlife | Relaxation | Emergency)",
            Assert.Single(errors));
    }

    [Fact]
    public void JsonElementReader_FallsBackToTheDefaultForAnAbsentNumber()
    {
        var errors = new List<string>();

        using var document = JsonDocument.Parse("""{"party_size":"five"}""");

        Assert.Equal(4, JsonElementReader.ReadInt(document.RootElement, "party_size", errors, fallback: 4));
        Assert.Contains("field 'party_size' must be a number", errors);
    }

    [Fact]
    public void JsonElementReader_SkipsBlankEntriesInAList()
    {
        var errors = new List<string>();

        using var document = JsonDocument.Parse("""{"interests":["souks", "  ", "rooftops"]}""");

        var interests = JsonElementReader.ReadStringList(document.RootElement, "interests", errors);

        Assert.Equal(new[] { "souks", "rooftops" }, interests);
        Assert.Empty(errors);
    }

    [Fact]
    public void JsonElementReader_ReadsTheFirstPresentString()
    {
        using var document = JsonDocument.Parse("""{"profile":{"avoid_list":[],"avoidlist":"crowds"}}""");

        // The second name is the spelling this profile actually uses; the first is only a shape that
        // cannot be read as a string, so it is skipped rather than returning nothing.
        Assert.Equal("crowds", JsonElementReader.ReadProfileString(document.RootElement.GetProperty("profile"), "avoid_list", "avoidlist"));
        Assert.Null(JsonElementReader.ReadProfileString(document.RootElement.GetProperty("profile"), "avoid_list"));
    }

    [Fact]
    public void Conversions_RoundTripAListOfValuesContainingSeparators()
    {
        var values = new[] { "old town souks, cafés", "rooftop & terrace", "" };

        var stored = Assert.IsType<string>(Conversions.StringList.ConvertToProvider(values));
        var restored = Assert.IsAssignableFrom<IEnumerable<string>>(
            Conversions.StringList.ConvertFromProvider(stored));

        // One JSON cell, not a comma-joined string: the values keep their own separators.
        Assert.Equal(3, JsonDocument.Parse(stored).RootElement.GetArrayLength());
        Assert.Equal(values, restored);
    }

    [Fact]
    public void Conversions_FallBackToAnEmptyListRatherThanThrowingOnACorruptCell()
    {
        var corrupt = Assert.IsAssignableFrom<IEnumerable<string>>(Conversions.StringList.ConvertFromProvider("{not json"));
        var blank = Assert.IsAssignableFrom<IEnumerable<string>>(Conversions.StringList.ConvertFromProvider(string.Empty));

        Assert.Empty(corrupt);
        Assert.Empty(blank);
    }

    [Fact]
    public void Conversions_WriteAnEmptyJsonArrayRatherThanTheStringNull()
    {
        // A null list used to be stored as the four characters "null", which is not valid JSON for
        // anything that reads the cell without this converter (including SQL json functions).
        var stored = Assert.IsType<string>(Conversions.StringList.ConvertToProvider(null!));

        Assert.Equal("[]", stored);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<string>>(Conversions.StringList.ConvertFromProvider(stored)));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<string>>(Conversions.StringList.ConvertFromProvider("null")));
    }

    [Fact]
    public void Conversions_StoreAnEnumByNameAndReadItBack()
    {
        var converter = Conversions.EnumByName<VenueType>();

        Assert.Equal("Indoor", converter.ConvertToProvider(VenueType.Indoor));
        Assert.Equal(VenueType.Outdoor, converter.ConvertFromProvider("Outdoor"));
        Assert.Equal(default(VenueType), converter.ConvertFromProvider("Teleport"));
    }
}
