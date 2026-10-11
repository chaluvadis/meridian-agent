namespace MeridianAgent.Tests;

using MeridianAgent.Application;

using MeridianAgent.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

public class ChatHistoryServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;

    public ChatHistoryServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "meridian-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "chat.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Windows/CI can hold the file a moment longer; nothing to clean up after a test run.
        }
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private ChatHistoryService CreateService()
    {
        var factory = new TestDbContextFactory(Options());

        using var context = factory.CreateDbContext();
        context.Database.Migrate();

        // The route guard only lets the chat render once onboarding has written a profile row, so
        // the traveler exists before their first day does.
        context.Profiles.Add(UserProfile.Default with { Id = IUserProfileService.DefaultProfileId });
        context.SaveChanges();

        return new ChatHistoryService(factory, NullLogger<ChatHistoryService>.Instance);
    }

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_databasePath}").Options;

    private async Task<int> SeedSessionAsync(DateOnly date, int dayNumber = 1)
    {
        await using var context = new TestDbContextFactory(Options()).CreateDbContext();

        var session = new ChatSession { Date = date, DayNumber = dayNumber };
        context.ChatSessions.Add(session);
        await context.SaveChangesAsync();

        return session.Id;
    }

    private async Task<int> CountSessionsAsync()
    {
        await using var context = new TestDbContextFactory(Options()).CreateDbContext();

        return await context.ChatSessions.CountAsync();
    }

    private static ChatMessageEntity Message(string role, string content, DateTime timestamp, int sessionId = 0) => new()
    {
        SessionId = sessionId,
        Role = role,
        Content = content,
        Timestamp = timestamp,
    };

    private static DateTime At(int hour, int minute) => Today.ToDateTime(new TimeOnly(hour, minute));

    [Fact]
    public async Task GetCurrentSessionAsync_OpensADayForToday()
    {
        var service = CreateService();

        var session = await service.GetCurrentSessionAsync();

        Assert.Equal(Today, session.Date);
        Assert.Equal(1, session.DayNumber);
        Assert.Equal(IChatHistoryService.DefaultProfileId, session.UserProfileId);
    }

    [Fact]
    public async Task GetCurrentSessionAsync_ReusesTheSameDayOnEveryCall()
    {
        var service = CreateService();
        var opened = await service.GetCurrentSessionAsync();
        await service.SaveMessageAsync(Message("user", "We just landed", At(20, 45)));

        var again = await service.GetCurrentSessionAsync();

        Assert.Equal(opened.Id, again.Id);
        Assert.Equal(1, await CountSessionsAsync());
    }

    [Fact]
    public async Task GetCurrentSessionAsync_NumbersTheDayFromTheFirstChatDay()
    {
        var service = CreateService();
        await SeedSessionAsync(Today.AddDays(-3));

        var session = await service.GetCurrentSessionAsync();

        Assert.Equal(4, session.DayNumber);
    }

    [Fact]
    public async Task GetSessionsAsync_ReturnsEveryDayOldestFirstWithItsMessages()
    {
        var service = CreateService();
        var earlier = await SeedSessionAsync(Today.AddDays(-3));
        await service.SaveMessageAsync(Message("user", "We just landed", At(9, 0), earlier));
        await service.SaveMessageAsync(Message("assistant", "Welcome to Dubai!", At(9, 1), earlier));
        await service.SaveMessageAsync(Message("user", "Anywhere for shawarma?", At(20, 45)));

        var sessions = await service.GetSessionsAsync();

        Assert.Equal(2, sessions.Count);
        Assert.Equal(Today.AddDays(-3), sessions[0].Date);
        Assert.Equal(2, sessions[0].Messages.Count);
        Assert.Equal(Today, sessions[1].Date);
        Assert.Single(sessions[1].Messages);
    }

    [Fact]
    public async Task SaveMessageAsync_WithNoSessionId_FilesTheMessageUnderToday()
    {
        var service = CreateService();
        var session = await service.GetCurrentSessionAsync();

        var saved = await service.SaveMessageAsync(Message("assistant", "Top pick: Karak House.", At(20, 45)));

        Assert.NotEqual(0, saved.Id);
        Assert.Equal(session.Id, saved.SessionId);

        var stored = await service.GetMessagesForSessionAsync(session.Id);
        var reloaded = Assert.Single(stored);
        Assert.Equal("Top pick: Karak House.", reloaded.Content);
        Assert.False(reloaded.IsFavorite);
        Assert.Null(reloaded.RichContentJson);
    }

    [Fact]
    public async Task SaveMessageAsync_WithASessionId_WritesToThatDay()
    {
        var service = CreateService();
        var yesterday = await SeedSessionAsync(Today.AddDays(-1));

        var saved =         await service.SaveMessageAsync(
            Message("user", "We just landed", At(9, 0), yesterday));

        var today = await service.GetCurrentSessionAsync();

        Assert.Equal(yesterday, saved.SessionId);
        Assert.Equal(2, today.DayNumber);
        Assert.Equal("We just landed", Assert.Single(await service.GetMessagesForSessionAsync(yesterday)).Content);
        Assert.Empty(await service.GetMessagesForSessionAsync(today.Id));
    }

    [Fact]
    public async Task SaveMessageAsync_RejectsANullMessage()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.SaveMessageAsync(null!));
    }

    [Fact]
    public async Task ToggleFavoriteAsync_FlipsTheBookmarkAndPersistsIt()
    {
        var service = CreateService();
        var saved = await service.SaveMessageAsync(Message("assistant", "Top pick: Karak House.", At(20, 45)));
        Assert.False(saved.IsFavorite);

        Assert.True(await service.ToggleFavoriteAsync(saved.Id));

        var reloaded = Assert.Single(await service.GetMessagesForSessionAsync(saved.SessionId));
        Assert.True(reloaded.IsFavorite);

        Assert.False(await service.ToggleFavoriteAsync(saved.Id));
        Assert.False(Assert.Single(await service.GetMessagesForSessionAsync(saved.SessionId)).IsFavorite);
    }

    [Fact]
    public async Task ToggleFavoriteAsync_ReturnsNullForAnUnknownMessage()
    {
        var service = CreateService();

        Assert.Null(await service.ToggleFavoriteAsync(4242));
    }

    [Fact]
    public async Task GetFavoritesAsync_ReturnsBookmarkedMessagesNewestFirst()
    {
        var service = CreateService();
        var noon = await service.SaveMessageAsync(Message("assistant", "the noon answer", At(12, 0)));
        var ten = await service.SaveMessageAsync(Message("assistant", "the ten answer", At(10, 0)));
        var tenAgain = await service.SaveMessageAsync(Message("assistant", "the other ten answer", At(10, 0)));

        await service.ToggleFavoriteAsync(noon.Id);
        await service.ToggleFavoriteAsync(ten.Id);
        await service.ToggleFavoriteAsync(tenAgain.Id);

        var favorites = await service.GetFavoritesAsync();

        Assert.Equal(
            new[] { "the noon answer", "the other ten answer", "the ten answer" },
            favorites.Select(favorite => favorite.Content));
        Assert.All(favorites, favorite => Assert.True(favorite.IsFavorite));
        Assert.NotNull(favorites[0].Session);
        Assert.Equal(Today, favorites[0].Session!.Date);
    }

    [Fact]
    public async Task GetFavoritesAsync_LeavesEverythingThatWasNotBookmarked()
    {
        var service = CreateService();
        var first = await service.SaveMessageAsync(Message("assistant", "bookmarked", At(12, 0)));
        await service.SaveMessageAsync(Message("user", "a question", At(11, 0)));
        await service.SaveMessageAsync(Message("assistant", "not bookmarked", At(10, 0)));
        await service.ToggleFavoriteAsync(first.Id);

        var favorites = await service.GetFavoritesAsync();

        Assert.Equal("bookmarked", Assert.Single(favorites).Content);
    }

    [Fact]
    public async Task GetMessagesForSessionAsync_ReturnsTheDayInTheOrderItHappened()
    {
        var service = CreateService();
        var session = await service.GetCurrentSessionAsync();
        await service.SaveMessageAsync(Message("user", "the question", At(10, 0), session.Id));
        await service.SaveMessageAsync(Message("assistant", "the answer", At(10, 5), session.Id));
        await service.SaveMessageAsync(Message("user", "said earlier in the day", At(9, 30), session.Id));

        var messages = await service.GetMessagesForSessionAsync(session.Id);

        Assert.Equal(
            new[] { "said earlier in the day", "the question", "the answer" },
            messages.Select(message => message.Content));
    }

    [Fact]
    public async Task GetMessagesForSessionAsync_IsEmptyForADayThatDoesNotExist()
    {
        var service = CreateService();

        Assert.Empty(await service.GetMessagesForSessionAsync(4242));
    }

    [Fact]
    public async Task OneDayCannotHaveTwoSessions()
    {
        CreateService();

        await using var context = new TestDbContextFactory(Options()).CreateDbContext();

        context.ChatSessions.Add(new ChatSession { Date = Today, DayNumber = 1 });
        await context.SaveChangesAsync();

        context.ChatSessions.Add(new ChatSession { Date = Today, DayNumber = 1 });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("UNIQUE constraint failed", error.InnerException?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ABookmarkReopenedLaterStillShowsItsChipsAndPlaces()
    {
        var service = CreateService();
        var saved = await service.SaveMessageAsync(ChatMessageCodec.ToEntity(
            "assistant",
            "Top pick: Karak House.",
            At(20, 45),
            new[]
            {
                new QuickAction("📌 Show on Map", "Karak House", QuickActionType.Navigate),
                new QuickAction("🔄 Find Alternatives", "somewhere calmer", QuickActionType.RefineSearch),
            },
            RichContent.FromItinerary(TestData.Itinerary)));

        await service.ToggleFavoriteAsync(saved.Id);

        var stored = Assert.Single(await service.GetMessagesForSessionAsync(saved.SessionId));

        var restored = ChatMessageCodec.ToDomain(stored);
        Assert.True(restored.IsFavorite);
        Assert.Equal(2, restored.QuickActions!.Count);
        Assert.Equal(QuickActionType.Navigate, restored.QuickActions[0].ActionType);
        Assert.Equal(
            "Karak House",
            Assert.Single(ChatMessageCodec.ReadRichContent(stored)!.Blocks).Text);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

        public AppDbContext CreateDbContext() => new(_options);
    }
}
