namespace MeridianAgent.Domain.Entities;

/// <summary>
/// One day of conversation — the unit the chat transcript is organised into. A session is created
/// lazily the first time a message is written on a given calendar day, so "Day 2 - October 13"
/// groups everything the traveler and the agent said that day.
/// </summary>
public sealed class ChatSession
{
    public int Id { get; set; }

    /// <summary>The local calendar day this session covers.</summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Which day of the trip this is, counting from the traveler's first chat day. The first day is
    /// 1; a two-day gap between conversations therefore shows up honestly as a jump in day number.
    /// </summary>
    public int DayNumber { get; set; }

    /// <summary>The traveler this session belongs to; a foreign key onto the profile row.</summary>
    public string UserProfileId { get; set; } = "default";

    public ICollection<ChatMessageEntity> Messages { get; set; } = new List<ChatMessageEntity>();
}
