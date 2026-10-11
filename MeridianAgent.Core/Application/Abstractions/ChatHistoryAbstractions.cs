namespace MeridianAgent.Application.Abstractions;

using MeridianAgent.Domain.Entities;

/// <summary>
/// Day-wise chat history, persisted so the transcript survives a reload and can be browsed and
/// bookmarked. Implemented over SQLite with EF Core and a short-lived context per operation, so it
/// is safe to share as a singleton across Blazor Server circuits.
/// </summary>
public interface IChatHistoryService
{
    /// <summary>The traveler the history is kept under: one profile, one set of days.</summary>
    const string DefaultProfileId = "default";

    /// <summary>Today's session, created on first use.</summary>
    Task<ChatSession> GetCurrentSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a message and returns it with the id the database assigned. A message whose
    /// <see cref="ChatMessageEntity.SessionId" /> is unresolved is filed into today's session so
    /// callers never have to know which day they are writing to.
    /// </summary>
    Task<ChatMessageEntity> SaveMessageAsync(ChatMessageEntity message, CancellationToken cancellationToken = default);

    /// <summary>Flips a message's bookmark. Returns the new state, or null when no such message exists.</summary>
    Task<bool?> ToggleFavoriteAsync(int messageId, CancellationToken cancellationToken = default);

    /// <summary>Every bookmarked message, newest first.</summary>
    Task<IReadOnlyList<ChatMessageEntity>> GetFavoritesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Today's bookmarked answers only, oldest first. This is what "compile my itinerary" reads, so
    /// the plan is built from what was saved today rather than everything saved ever.
    /// </summary>
    Task<IReadOnlyList<ChatMessageEntity>> GetFavoritesForCurrentSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>All days with history, oldest first, each carrying its messages.</summary>
    Task<IReadOnlyList<ChatSession>> GetSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>One day's transcript, in the order it happened.</summary>
    Task<IReadOnlyList<ChatMessageEntity>> GetMessagesForSessionAsync(int sessionId, CancellationToken cancellationToken = default);
}
