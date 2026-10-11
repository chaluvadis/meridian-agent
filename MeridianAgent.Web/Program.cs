using MeridianAgent.Web.Hubs;

using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.Configure<QdrantOptions>(builder.Configuration.GetSection(QdrantOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<NominatimOptions>(builder.Configuration.GetSection(NominatimOptions.SectionName));
builder.Services.Configure<OsrmOptions>(builder.Configuration.GetSection(OsrmOptions.SectionName));
builder.Services.Configure<OverpassOptions>(builder.Configuration.GetSection(OverpassOptions.SectionName));
builder.Services.Configure<WeatherOptions>(builder.Configuration.GetSection(WeatherOptions.SectionName));
builder.Services.Configure<ProactiveSuggestionsOptions>(builder.Configuration.GetSection(ProactiveSuggestionsOptions.SectionName));
builder.Services.Configure<WeatherPivotOptions>(builder.Configuration.GetSection(WeatherPivotOptions.SectionName));
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
// MudBlazor: services plus the theme providers rendered by the layouts.
builder.Services.AddMudServices();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();
// Ollama: typed client with base address and timeout from configuration.
builder.Services
    .AddHttpClient<ILlmClient, OllamaClient>((serviceProvider, client) =>
    {
        var options = builder.Configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>() ?? new OllamaOptions();
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/'));
        client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
    });
// Qdrant vector store + external map/weather APIs.
builder.Services.AddHttpClient<QdrantClient>();
builder.Services.AddHttpClient<NominatimClient>();
builder.Services.AddHttpClient<WeatherClient>();
builder.Services.AddHttpClient<OsrmTravelClient>();
builder.Services.AddHttpClient<OverpassVenueSource>();
builder.Services.AddSingleton<ITravelContextProvider, LiveTravelContextProvider>();
// Legacy JSON profiles: read-only now, used to seed SQLite on first run.
builder.Services.AddSingleton<IUserProfileStore, JsonFileUserProfileStore>();
builder.Services.AddSingleton<IUserProfileService, UserProfileService>();
// Structured traveler state lives in SQLite. The factory lifetime keeps the singleton profile
// service safe in Blazor Server: contexts are short-lived and created per operation.
var meridianConnectionString = builder.Configuration.GetConnectionString("Meridian")
                               ?? MeridianDatabase.DefaultConnectionString;
builder.Services.AddMeridianDatabase(meridianConnectionString);
// Day-wise chat history. Singleton for the same reason as the profile service: the chat circuit,
// the route guard, and the Glance panel all share it, and every operation makes its own context.
builder.Services.AddSingleton<IChatHistoryService, ChatHistoryService>();
builder.Services.AddSingleton<IRulebookStore, JsonFileRulebookStore>();
// One store owns the choice of source: live venues around a shared position, sample file otherwise.
builder.Services.AddSingleton<INearbyVenueStore, CompositeNearbyVenueStore>();
// The composite asks the file-backed store first, so it needs it directly rather than through the
// interface, which is what lets the two be told apart.
builder.Services.AddSingleton<JsonFileNearbyVenueStore>();
// Both live sources are configured from their own sections so each can point at a container rather
// than a public endpoint, and both are singletons: they cache their answers on purpose.
builder.Services.AddSingleton<OverpassVenueSource>(services =>
    new OverpassVenueSource(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OverpassVenueSource)),
        services.GetRequiredService<IOptions<OverpassOptions>>(),
        services.GetRequiredService<ILogger<OverpassVenueSource>>()));
builder.Services.AddSingleton<ILiveVenueSource>(services =>
    services.GetRequiredService<OverpassVenueSource>());
builder.Services.AddSingleton<ITravelTimeProvider>(services =>
    new OsrmTravelClient(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OsrmTravelClient)),
        services.GetRequiredService<IOptions<OsrmOptions>>(),
        services.GetRequiredService<ILogger<OsrmTravelClient>>()));
builder.Services.AddSingleton<VectorRulebookNarrower>();
builder.Services.AddSingleton<IRulebookNarrower>(serviceProvider =>
    serviceProvider.GetRequiredService<VectorRulebookNarrower>());
builder.Services.AddScoped<IContextAgent, ContextAgent>();
builder.Services.AddScoped<ISearchAgent, SearchAgent>();
builder.Services.AddScoped<ISafeguardAgent, SafeguardAgent>();
builder.Services.AddScoped<ISynthesizerAgent, SynthesizerAgent>();
builder.Services.AddScoped<IProactiveSuggestionAgent, ProactiveSuggestionAgent>();
builder.Services.AddHostedService<RulebookIndexingService>();
builder.Services.AddHostedService<ProactiveSuggestionScheduler>();
builder.Services.AddSingleton<IChatActivityTracker, ChatActivityTracker>();
builder.Services.AddSingleton<IProactiveChatNotifier, HubChatNotifier>();
builder.Services.AddSingleton<WeatherPivotService>();
builder.Services.AddSingleton(new SignatureCooldown(TimeSpan.FromHours(4)));
builder.Services.AddScoped<AgentBlackboard>();
builder.Services.AddScoped<TravelQueryOrchestrator>();

builder.Services.AddScoped<ChatState>();
builder.Services.AddScoped<MapState>();
builder.Services.AddScoped<SpeechInterop>();
builder.Services.AddScoped<GeolocationInterop>();
var app = builder.Build();

// SQLite: apply pending migrations before the first request, then import the JSON profiles written
// by builds before SQLite so existing travelers keep their preferences.
await app.Services.GetRequiredService<ProfileDatabaseInitializer>().InitializeAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseSerilogRequestLogging();
app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapHub<ChatHub>("/chatHub");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
Log.Information("The Meridian Agent is starting up");
app.Run();