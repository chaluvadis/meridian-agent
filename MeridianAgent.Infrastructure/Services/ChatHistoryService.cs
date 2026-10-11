namespace MeridianAgent.Infrastructure.Services;

using MeridianAgent.Application;
using MeridianAgent.Application.Abstractions;
using MeridianAgent.Domain.Entities;
using MeridianAgent.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Day-wise chat history backed by SQLite. The service is a singleton (the route guard, the chat,
/// and the Glance panel all reach for it), so it takes an <see cref="IDbContextFactory{AppDbContext}" />
/// and creates a short-lived context per operation — the same pattern <see cref="UserProfileService" />
/// uses, which keeps Blazor Server's shared circuits safe.
/// </summary>
public sealed class ChatHistoryService : IChatHistoryService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ChatHistoryService> _logger;

    public ChatHistoryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ChatHistoryService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<ChatSession> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        var today = DayToday();

        var existing = await FindSessionAsync(today, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await CreateSessionAsync(today, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two messages crossed the midnight boundary together; the other one won the insert.
            var winner = await FindSessionAsync(today, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                ex,
                "A session for {Today} was created concurrently; reusing the existing row",
                today);

            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    private async Task<ChatSession?> FindSessionAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.ChatSessions
            .FirstOrDefaultAsync(session => session.Date == date, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ChatMessageEntity> SaveMessageAsync(
        ChatMessageEntity message,
        CancellationToken cancellationToken = default)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (message.SessionId == 0)
        {
            var session = await GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            message.SessionId = session.Id;
        }

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        context.ChatMessages.Add(message);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return message;
    }

    public async Task<bool?> ToggleFavoriteAsync(int messageId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var message = await context.ChatMessages
            .FirstOrDefaultAsync(stored => stored.Id == messageId, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
        {
            return null;
        }

        message.IsFavorite = !message.IsFavorite;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Message {MessageId} favourite set to {IsFavorite}",
            messageId,
            message.IsFavorite);

        return message.IsFavorite;
    }

    public async Task<IReadOnlyList<ChatMessageEntity>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.ChatMessages
            .AsNoTracking()
            .Where(message => message.IsFavorite)
            .OrderByDescending(message => message.Timestamp)
            .ThenByDescending(message => message.Id)
            .Include(message => message.Session)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatSession>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.ChatSessions
            .AsNoTracking()
            .OrderBy(session => session.Date)
            .ThenBy(session => session.Id)
            .Include(session => session.Messages)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatMessageEntity>> GetMessagesForSessionAsync(
        int sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.ChatMessages
            .AsNoTracking()
            .Where(message => message.SessionId == sessionId)
            .OrderBy(message => message.Timestamp)
            .ThenBy(message => message.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ChatSession> CreateSessionAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var session = new ChatSession
        {
            Date = date,
            DayNumber = await ResolveDayNumberAsync(context, date, cancellationToken).ConfigureAwait(false),
        };

        context.ChatSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Started chat day {DayNumber} on {Date}", session.DayNumber, session.Date);

        return session;
    }

    /// <summary>
    /// The day number is the distance from the traveler's first chat day, so a gap between
    /// conversations is visible rather than silently renumbered away.
    /// </summary>
    private static async Task<int> ResolveDayNumberAsync(
        AppDbContext context,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var firstDay = await context.ChatSessions
            .OrderBy(session => session.Date)
            .Select(session => (DateOnly?)session.Date)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return firstDay is null ? 1 : (date.DayNumber - firstDay.Value.DayNumber) + 1;
    }

    /// <summary>Today in local time, which is the calendar day the app already renders in.</summary>
    private static DateOnly DayToday() => DateOnly.FromDateTime(DateTime.Now);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
}
