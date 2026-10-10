using MeridianAgent.Web.Hubs;

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
builder.Services.Configure<WeatherOptions>(builder.Configuration.GetSection(WeatherOptions.SectionName));
builder.Services.Configure<ProactiveSuggestionsOptions>(builder.Configuration.GetSection(ProactiveSuggestionsOptions.SectionName));
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
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
builder.Services.AddHttpClient<OsrmClient>();
builder.Services.AddHttpClient<WeatherClient>();
builder.Services.AddSingleton<ITravelContextProvider, LiveTravelContextProvider>();
builder.Services.AddSingleton<IUserProfileStore, JsonFileUserProfileStore>();
builder.Services.AddSingleton<IRulebookStore, JsonFileRulebookStore>();
builder.Services.AddSingleton<INearbyVenueStore, JsonFileNearbyVenueStore>();
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
builder.Services.AddScoped<AgentBlackboard>();
builder.Services.AddScoped<TravelQueryOrchestrator>();
builder.Services.AddScoped<ChatState>();
builder.Services.AddScoped<MapState>();
var app = builder.Build();
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