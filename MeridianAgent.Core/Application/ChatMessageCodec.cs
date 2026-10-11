namespace MeridianAgent.Application;

using System.Text.Json;
using System.Text.Json.Serialization;

using MeridianAgent.Domain.Entities;

/// <summary>
/// Translation between the persisted row (<see cref="ChatMessageEntity" />) and the shape the chat
/// renders (<see cref="ChatMessage" />), including the JSON for the two structured payloads. Kept in
/// one place so the Web layer, the history service, and the tests all agree on the wire format.
/// </summary>
public static class ChatMessageCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Builds a row from a rendered message. <paramref name="sessionId" /> of zero means "file it
    /// under today's session", which is what the history service fills in on save.
    /// </summary>
    public static ChatMessageEntity ToEntity(
        ChatMessage message,
        int sessionId = 0,
        RichContent? richContent = null)
    {
        return ToEntity(
            message.Role,
            message.Content,
            message.Timestamp,
            message.QuickActions,
            richContent,
            message.IsFavorite,
            sessionId);
    }

    /// <summary>Builds a row from its parts, without a rendered message to wrap.</summary>
    public static ChatMessageEntity ToEntity(
        string role,
        string content,
        DateTime timestamp,
        IReadOnlyList<QuickAction>? quickActions = null,
        RichContent? richContent = null,
        bool isFavorite = false,
        int sessionId = 0)
    {
        return new ChatMessageEntity
        {
            SessionId = sessionId,
            Role = role,
            Content = content,
            Timestamp = timestamp,
            IsFavorite = isFavorite,
            QuickActionsJson = SerializeQuickActions(quickActions),
            RichContentJson = richContent is null || richContent.IsEmpty
                ? null
                : JsonSerializer.Serialize(richContent, Options),
        };
    }

    /// <summary>Rebuilds the rendered message from a stored row.</summary>
    public static ChatMessage ToDomain(ChatMessageEntity entity)
    {
        return new ChatMessage(
            entity.Id.ToString(),
            entity.Role,
            entity.Content,
            entity.Timestamp,
            MessageType.Reactive,
            DeserializeQuickActions(entity.QuickActionsJson),
            entity.Id,
            entity.IsFavorite);
    }

    /// <summary>Reads the structured blocks back out of a stored row, never throwing on bad JSON.</summary>
    public static RichContent? ReadRichContent(ChatMessageEntity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.RichContentJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RichContent>(entity.RichContentJson, Options);
        }
        catch (JsonException)
        {
            // A bookmark with an unreadable payload still shows its text.
            return null;
        }
    }

    /// <summary>Reads the chips back out of a stored row, never throwing on bad JSON.</summary>
    public static List<QuickAction>? DeserializeQuickActions(string? json) =>
        ParseList<QuickActionDto>(json) is { Count: > 0 } dtos
            ? dtos.Select(dto => new QuickAction(dto.Label, dto.Payload, QuickActionTypeExtensions.FromModelString(dto.ActionType)))
                .ToList()
            : null;

    /// <summary>Writes the chips out as JSON, or null when there is nothing to write.</summary>
    public static string? SerializeQuickActions(IReadOnlyList<QuickAction>? actions)
    {
        if (actions is not { Count: > 0 })
        {
            return null;
        }

        var dtos = actions.Select(action => new QuickActionDto(
            action.Label,
            action.Payload,
            action.ActionType.ToModelString()));

        return JsonSerializer.Serialize(dtos, Options);
    }

    private static List<T>? ParseList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Wire shape for a chip; the payload is invariant, the action type is a model string.</summary>
    private sealed record QuickActionDto(string Label, string Payload, string ActionType);
}
