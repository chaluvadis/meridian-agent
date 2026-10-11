namespace MeridianAgent.Tests;

using MeridianAgent.Application;

public class ChatMessageCodecTests
{
    private static readonly DateTime Stamp = new(2026, 10, 12, 14, 30, 0, DateTimeKind.Local);

    private static List<QuickAction> Chips => new()
    {
        new QuickAction("📌 Show on Map", "Karak House", QuickActionType.Navigate),
        new QuickAction("✅ Accept & Plan", "Plan an evening around Karak House", QuickActionType.AcceptSuggestion),
        new QuickAction("🔄 Find Alternatives", "somewhere calmer than Karak House", QuickActionType.RefineSearch),
    };

    private static ChatMessage Rendered() => new(
        "m-1",
        "assistant",
        "Top pick: Karak House, six minutes away.",
        Stamp,
        MessageType.Reactive,
        Chips,
        StoredId: 42,
        IsFavorite: true);

    [Fact]
    public void ToEntity_FromRenderedMessage_CopiesEveryFieldOntoTheRow()
    {
        var entity = ChatMessageCodec.ToEntity(
            Rendered(),
            sessionId: 7,
            richContent: RichContent.FromItinerary(TestData.Itinerary));

        Assert.Equal(7, entity.SessionId);
        Assert.Equal("assistant", entity.Role);
        Assert.Equal("Top pick: Karak House, six minutes away.", entity.Content);
        Assert.Equal(Stamp, entity.Timestamp);
        Assert.True(entity.IsFavorite);
        Assert.NotNull(entity.QuickActionsJson);
        Assert.NotNull(entity.RichContentJson);
    }

    [Fact]
    public void ToEntity_FromParts_WritesTheSameRowAsFromARenderedMessage()
    {
        var fromMessage = ChatMessageCodec.ToEntity(Rendered(), sessionId: 7);
        var fromParts = ChatMessageCodec.ToEntity(
            "assistant",
            Rendered().Content,
            Stamp,
            Chips,
            richContent: null,
            isFavorite: true,
            sessionId: 7);

        Assert.Equal(fromMessage.QuickActionsJson, fromParts.QuickActionsJson);
        Assert.Equal(fromMessage.Role, fromParts.Role);
        Assert.Equal(fromMessage.Timestamp, fromParts.Timestamp);
    }

    [Fact]
    public void ToEntity_StoresNullJsonWhenThereIsNothingToStore()
    {
        var plain = ChatMessageCodec.ToEntity(new ChatMessage("m-1", "user", "We just landed", Stamp));
        var noVenues = ChatMessageCodec.ToEntity("assistant", "Nothing fits tonight", Stamp, Chips, RichContent.Empty);
        var emptyChips = ChatMessageCodec.ToEntity("assistant", "No chips", Stamp, Array.Empty<QuickAction>());

        Assert.Null(plain.QuickActionsJson);
        Assert.Null(plain.RichContentJson);
        Assert.Null(noVenues.RichContentJson);
        Assert.Null(emptyChips.QuickActionsJson);
    }

    [Fact]
    public void ToDomain_RestoresTheRenderedMessage()
    {
        var entity = ChatMessageCodec.ToEntity(Rendered(), sessionId: 3);
        entity.Id = 99;

        var restored = ChatMessageCodec.ToDomain(entity);

        Assert.Equal("99", restored.Id);
        Assert.Equal("assistant", restored.Role);
        Assert.Equal(Rendered().Content, restored.Content);
        Assert.Equal(Stamp, restored.Timestamp);
        Assert.Equal(99, restored.StoredId);
        Assert.True(restored.IsFavorite);
        Assert.Equal(Chips, restored.QuickActions);
    }

    [Fact]
    public void QuickActions_SurviveARoundTripWithTheirActionTypes()
    {
        var entity = ChatMessageCodec.ToEntity("assistant", "Top pick: Karak House.", Stamp, Chips);

        var restored = ChatMessageCodec.ToDomain(entity).QuickActions;

        Assert.NotNull(restored);
        Assert.Equal(Chips, restored);
        Assert.Equal(QuickActionType.Navigate, restored[0].ActionType);
        Assert.Equal(QuickActionType.AcceptSuggestion, restored[1].ActionType);
        Assert.Equal(QuickActionType.RefineSearch, restored[2].ActionType);
    }

    [Fact]
    public void SerializeQuickActions_UsesCamelCaseWireNames()
    {
        var json = ChatMessageCodec.SerializeQuickActions(
            new[] { new QuickAction("Show on Map", "Karak House", QuickActionType.Navigate) });

        Assert.NotNull(json);
        Assert.Contains("\"label\":\"Show on Map\"", json);
        Assert.Contains("\"payload\":\"Karak House\"", json);
        Assert.Contains("\"actionType\":\"navigate\"", json);
        Assert.DoesNotContain("\"ActionType\"", json);
    }

    [Theory]
    [InlineData("show_map")]
    [InlineData("open the map")]
    [InlineData("view_by_name")]
    public void DeserializeQuickActions_ToleratesLooseNavigateStrings(string raw)
    {
        var restored = ChatMessageCodec.DeserializeQuickActions(
            $"[{{\"label\":\"Pin it\",\"payload\":\"Karak House\",\"actionType\":\"{raw}\"}}]");

        Assert.NotNull(restored);
        var chip = Assert.Single(restored!);
        Assert.Equal(QuickActionType.Navigate, chip.ActionType);
        Assert.Equal("Karak House", chip.Payload);
    }

    [Theory]
    [InlineData("plan it")]
    [InlineData("accept_suggestion")]
    [InlineData("take me there")]
    public void DeserializeQuickActions_ToleratesLooseAcceptStrings(string raw)
    {
        var restored = ChatMessageCodec.DeserializeQuickActions(
            $"[{{\"label\":\"Plan it\",\"payload\":\"Karak House\",\"actionType\":\"{raw}\"}}]");

        Assert.NotNull(restored);
        var chip = Assert.Single(restored!);
        Assert.Equal(QuickActionType.AcceptSuggestion, chip.ActionType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("flibbertigibbet")]
    public void DeserializeQuickActions_FallsBackToRefineSearchForAnythingUnrecognised(string? raw)
    {
        var restored = ChatMessageCodec.DeserializeQuickActions(
            $"[{{\"label\":\"Do it\",\"payload\":\"Karak House\",\"actionType\":{(raw is null ? "null" : $"\"{raw}\"")}}}]");

        Assert.NotNull(restored);
        var chip = Assert.Single(restored!);
        Assert.Equal(QuickActionType.RefineSearch, chip.ActionType);
    }

    [Fact]
    public void DeserializeQuickActions_IsNullWhenThereIsNothingUsable()
    {
        Assert.Null(ChatMessageCodec.DeserializeQuickActions(null));
        Assert.Null(ChatMessageCodec.DeserializeQuickActions("   "));
        Assert.Null(ChatMessageCodec.DeserializeQuickActions("[]"));
        Assert.Null(ChatMessageCodec.DeserializeQuickActions("not json at all"));
    }

    [Fact]
    public void ReadRichContent_RoundTripsThePlacesAReplyProposed()
    {
        var entity = ChatMessageCodec.ToEntity(
            "assistant",
            "Top pick: Karak House.",
            Stamp,
            richContent: RichContent.FromItinerary(TestData.Itinerary));

        var rich = ChatMessageCodec.ReadRichContent(entity);

        Assert.NotNull(rich);
        var block = Assert.Single(rich!.Blocks);
        Assert.Equal(RichBlockKind.List, block.Kind);
        Assert.Equal("Karak House", block.Text);
        Assert.Equal("Cafe · Indoor · 6 min away", block.Detail);
    }

    [Fact]
    public void ReadRichContent_IsNullForAMessageThatProposedNothing()
    {
        var none = ChatMessageCodec.ToEntity("user", "We just landed", Stamp);

        Assert.Null(ChatMessageCodec.ReadRichContent(none));
        Assert.Null(ChatMessageCodec.ReadRichContent(new ChatMessageEntity { RichContentJson = "   " }));
    }

    [Fact]
    public void ReadRichContent_ReturnsNullOnMalformedJsonInsteadOfThrowing()
    {
        var broken = new ChatMessageEntity { RichContentJson = "[{ \"blocks\": [oops" };

        Assert.Null(ChatMessageCodec.ReadRichContent(broken));
    }
}
