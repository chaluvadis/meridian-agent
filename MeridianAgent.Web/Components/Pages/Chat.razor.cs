namespace MeridianAgent.Web.Components.Pages
{
    public partial class Chat
    {
        private readonly string[] _suggestions = new[]
        {
        "I've been walking all day - somewhere calm for dinner?",
        "We're exhausted, anywhere good nearby?",
        "Want to see the old souks before they close?",
    };

        private HubConnection? _hubConnection;
        private ElementReference _messagesContainer;
        private string _query = string.Empty;
        private bool _showMap;
        private bool _listening;
        private bool _speechSupported;
        private string? _speakingMessageId;
        private UserProfile _profile = UserProfile.Default;
        private DestinationRulebook? _rulebook;
        private string _profileName = "Traveler";
        private TravelContext? _context;
        private Task? _welcomeTask;

        /// <summary>
        /// The hour as the destination experiences it, so the greeting and the clock shown beside it
        /// can never disagree. Falls back to the server clock only when no context has loaded yet.
        /// </summary>
        private int LocalHour =>
            int.TryParse(_context?.LocalTime?.Split(':')[0], out var parsed) ? parsed : DateTime.Now.Hour;

        private string TimeOfDay => LocalHour switch
        {
            < 12 => "morning",
            < 17 => "afternoon",
            _ => "evening",
        };

        private string GreetingMessage => TimeOfDay switch
        {
            "morning" => "Ready for today's adventures?",
            "afternoon" => "How's your day going?",
            _ => "Looking for evening plans?",
        };

        private string _firstName => _profileName.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts
            ? parts[0]
            : _profileName;

        /// <summary>The empty state's promise: this app already knows who it is talking to.</summary>
        private string _emptyStatePitch
        {
            get
            {
                var interests = _profile.CoreInterests.Count > 0 ? _profile.CoreInterests : _profile.Interests;
                var style = string.IsNullOrWhiteSpace(_profile.TravelStyle) ? "curious" : _profile.TravelStyle.ToLowerInvariant();
                var interest = interests.Count > 0 ? interests[0].ToLowerInvariant() : "the city";

                return $"I know your style — a {style} traveler who loves {interest}. Tell me how you are feeling, or start with one of the suggestions below.";
            }
        }

        private string WeatherIcon => _context switch
        {
            null => Icons.Material.Filled.WbSunny,
            { IsRaining: true } => Icons.Material.Filled.Water,
            { WeatherCondition: "Clear" } => Icons.Material.Filled.WbSunny,
            { WeatherCondition: "Clouds" } => Icons.Material.Filled.Cloud,
            { WeatherCondition: "Thunderstorm" } => Icons.Material.Filled.Thunderstorm,
            { WeatherCondition: "Snow" } => Icons.Material.Filled.AcUnit,
            _ => Icons.Material.Filled.Cloud,
        };

        protected override async Task OnInitializedAsync()
        {
            ChatState.Changed += OnStateChanged;

            // Voice is an extra, never a requirement: a browser without the Web Speech API keeps the
            // microphone disabled rather than showing a button that does nothing.
            try
            {
                _speechSupported = await Speech.IsSupportedAsync();
            }
            catch (Exception)
            {
                _speechSupported = false;
            }

            Speech.UtteranceFinal += OnUtteranceFinalAsync;
            Speech.ListeningStopped += OnListeningStoppedAsync;
            Speech.ListeningFailed += OnListeningFailedAsync;
            Speech.SpeakingFinished += OnSpeakingFinishedAsync;

            _profile = await ProfileService.GetProfileAsync("default") ?? UserProfile.Default;
            _profileName = _profile.DisplayName;

            if (!string.IsNullOrWhiteSpace(_profile.Destination))
            {
                _rulebook = await RulebookStore.GetAsync(_profile.Destination);
            }

            // Today's transcript, so a refresh (or a new tab) picks up where the traveler left off.
            // This runs during prerender too, which is what puts the history into the first paint.
            await LoadTodayAsync();

            // The header shows the city as it is right now, so the greeting, the clock and the weather
            // it paints all come from the same place the pipeline reads. The provider caches briefly,
            // so prerendering plus the interactive circuit plus the first query cost one round trip,
            // not three.
            try
            {
                var location = _profile.EffectiveLocation;

                if (!string.IsNullOrWhiteSpace(location))
                {
                    _context = await ContextProvider.GetCurrentAsync(location);
                }
            }
            catch (Exception)
            {
                // Weather is decoration, never a reason to leave the chat blank.
            }

            // The greeting is produced once, when the traveler first lands in chat after onboarding.
            // During prerender HttpContext is present; the work belongs to the interactive circuit,
            // otherwise it would run twice and the flag would be cleared before the message was shown.
            if (_profile.PendingWelcome && HttpContextAccessor.HttpContext is null)
            {
                _welcomeTask = GreetAsync(_profile);
            }

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(Navigation.ToAbsoluteUri("/chatHub"))
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<string, string, string>(
                "ReceiveProactiveMessage",
                (messageId, content, kind) =>
                {
                    // A weather pivot is not a gentle nudge: it is the agent saying a saved plan has a
                    // problem, and it is tagged so the chat can show it that way.
                    var type = string.Equals(kind, "weatherPivot", StringComparison.OrdinalIgnoreCase)
                        ? MessageType.WeatherPivot
                        : MessageType.Proactive;

                    ChatState.AddProactive(messageId, content, type);

                    // Reading aloud through a weather alarm would bury it, so the voice steps aside.
                    _ = StopSpeakingAsync();

                    // A proactive message is agent activity: keep the session fresh enough for more.
                    Activity.RecordActivity();

                    return InvokeAsync(async () =>
                    {
                        StateHasChanged();
                        await ScrollToBottomAsync();
                    });
                });

            try
            {
                await _hubConnection.StartAsync();
                Activity.RecordActivity();
            }
            catch (Exception)
            {
                // The chat still works without the hub; only proactive messages need it.
            }
        }

        private void OnStateChanged()
        {
            InvokeAsync(StateHasChanged);
        }

        protected override Task OnAfterRenderAsync(bool firstRender) =>
            BrowserTitle.SetAsync(JSRuntime, firstRender, "Chat — The Meridian Agent");

        private async Task OnKeyDown(KeyboardEventArgs args)
        {
            if (args.Key == "Enter")
            {
                await SendAsync(_query);
            }
        }

        private void ToggleMap() => _showMap = !_showMap;

        /// <summary>
        /// Opens or closes the microphone. A refused permission or a browser with no speech engine
        /// leaves the button off rather than stuck half-on, so the traveler can always retry.
        /// </summary>
        private async Task ToggleListeningAsync()
        {
            if (!_speechSupported)
            {
                return;
            }

            if (_listening)
            {
                await Speech.StopListeningAsync();
                _listening = false;
                return;
            }

            if (ChatState.IsBusy)
            {
                return;
            }

            var started = await Speech.StartListeningAsync();
            _listening = started;

            if (!started)
            {
                // The browser declined or the engine is unavailable: stop offering it rather than
                // letting the traveler find a button that never listens.
                _speechSupported = false;
            }
        }

        /// <summary>
        /// Voice-first: the transcript lands in the box and is sent on its own. Typing is still there
        /// for anyone who wants to edit first — the mic only fills the field, it never takes over it.
        /// </summary>
        private async Task OnUtteranceFinalAsync(string transcript)
        {
            await InvokeAsync(async () =>
            {
                _listening = false;
                _query = transcript;

                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    await SendAsync(transcript);
                    return;
                }

                StateHasChanged();
            });
        }

        private Task OnListeningStoppedAsync() => InvokeAsync(() =>
        {
            _listening = false;
            StateHasChanged();
        });

        private Task OnListeningFailedAsync(string reason)
        {
            // "no-speech" is the browser saying the traveler said nothing, which is not a failure worth
            // logging as an error; everything else (denied, network, audio-capture) is.
            if (!string.Equals(reason, "no-speech", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWarning("Speech recognition failed: {Reason}", reason);
            }

            _listening = false;
            return InvokeAsync(StateHasChanged);
        }

        /// <summary>
        /// Reads an answer aloud, or stops it. One message at a time: asking to hear something else
        /// replaces what is being said, so the traveler never hears two voices over each other.
        /// </summary>
        private async Task ToggleSpeakingAsync(ChatMessage message)
        {
            if (_speakingMessageId == message.Id)
            {
                await StopSpeakingAsync();
                StateHasChanged();
                return;
            }

            var speaking = await Speech.SpeakAsync(message.Content);

            _speakingMessageId = speaking ? message.Id : null;
            StateHasChanged();
        }

        private async Task StopSpeakingAsync()
        {
            await Speech.StopSpeakingAsync();
            _speakingMessageId = null;
        }

        private Task OnSpeakingFinishedAsync() => InvokeAsync(() =>
        {
            _speakingMessageId = null;
            StateHasChanged();
        });

        /// <summary>Reads today's day back into the transcript, replacing whatever is on screen.</summary>
        private async Task LoadTodayAsync()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var sessionId = await TodaySessionIdAsync();

            var stored = await History.GetMessagesForSessionAsync(sessionId);
            var messages = stored.Select(ChatMessageCodec.ToDomain).ToList();

            ChatState.LoadSession(today, messages, false);
        }

        private async Task ShowTodayAsync()
        {
            await LoadTodayAsync();
            StateHasChanged();
            await ScrollToBottomAsync();
        }

        /// <summary>
        /// The session new messages belong to. The history service resolves it rather than the page
        /// holding it, so there is one owner of "which day is today".
        /// </summary>
        private async Task<int> TodaySessionIdAsync()
        {
            var session = await History.GetCurrentSessionAsync();
            return session.Id;
        }

        /// <summary>
        /// Bookmarks an answer. The flip is applied to the transcript first so the heart responds to
        /// the tap, and the write is queued behind it — if the database is unhappy the UI does not hang.
        /// </summary>
        private void ToggleFavoriteAsync(ChatMessage message)
        {
            if (message.StoredId is not int id)
            {
                return;
            }

            var desired = !message.IsFavorite;
            ChatState.SetFavorite(message.Id, desired);

            _ = PersistFavoriteAsync(id, desired, message.Id);
        }

        private async Task PersistFavoriteAsync(int messageId, bool desired, string chatMessageId)
        {
            try
            {
                var stored = await History.ToggleFavoriteAsync(messageId);

                // A concurrent write may have moved the flag; the database is the truth.
                if (stored is not null)
                {
                    ChatState.SetFavorite(chatMessageId, stored.Value);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not bookmark message {MessageId}", messageId);
                ChatState.SetFavorite(chatMessageId, !desired);
            }
        }

        /// <summary>
        /// The agent's first words after onboarding: generated by the Synthesizer from everything the
        /// traveler just shared, with a templated fallback so the chat never opens silently.
        /// </summary>
        private async Task GreetAsync(UserProfile profile)
        {
            var welcome = string.Empty;

            try
            {
                welcome = await Synthesizer.ExecuteWelcomeAsync(profile);
            }
            catch (Exception)
            {
                // LLM hiccup: the warm fallback still proves the profile was heard.
            }

            try
            {
                // Clear the flag first so a closed tab never repeats the greeting.
                await ProfileService.MarkWelcomeSeenAsync();

                if (string.IsNullOrWhiteSpace(welcome))
                {
                    welcome = profile.BuildWelcomeFallback();
                }

                await InvokeAsync(async () =>
                {
                    ChatState.BeginAssistant();
                    ChatState.AppendAssistant(welcome);
                    ChatState.EndAssistant();

                    StateHasChanged();
                    await ScrollToBottomAsync();
                });
            }
            catch (ObjectDisposedException)
            {
                // The traveler navigated away mid-greeting; nothing left to render.
            }
        }

        private async Task SendAsync(string query)
        {
            if (ChatState.IsBusy || string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            Activity.RecordActivity();
            await PingHubAsync();

            // A question asked while reading a past day belongs to today, so the view follows it back.
            if (ChatState.IsViewingHistory)
            {
                await LoadTodayAsync();
            }

            _query = string.Empty;
            var userMessageId = Guid.NewGuid().ToString();
            ChatState.AddUser(query);
            QueueSave(userMessageId, "user", query, null);

            ChatState.BeginAssistant();
            StateHasChanged();

            TravelRequest? request = null;
            TravelResponse? response = null;

            try
            {
                request = await BuildRequestAsync(query);
                response = await Orchestrator.SynthesizeAsync(request);

                // The reply is fully generated before anything is shown, so reveal it in a few beats
                // instead of an instant wall of text — and the chips land with the finished message.
                foreach (var chunk in RevealChunks(response.ResponseText))
                {
                    ChatState.AppendAssistant(chunk);
                    await Task.Delay(12);
                }
            }
            catch (OperationCanceledException)
            {
                ChatState.AppendAssistant("That's taken me too long — please ask again.");
            }
            catch (Exception ex)
            {
                ChatState.AppendAssistant($"I hit a snag: {ex.Message}");
            }
            finally
            {
                var quickActions = response?.QuickActions;
                ChatState.EndAssistant(quickActions);

                // The structured half of the reply — the places it proposed — is kept beside its text,
                // so a saved answer still shows what was on offer when it is opened weeks later.
                var itinerary = response?.Itinerary ?? Orchestrator.Blackboard.Itinerary ?? ProposedItinerary.Empty;

                if (ChatState.Messages.Count > 0)
                {
                    var last = ChatState.Messages[^1];

                    if (last.Role == "assistant")
                    {
                        QueueSave(last.Id, "assistant", last.Content, quickActions, itinerary);
                    }
                }

                var venues = itinerary.Venues ?? Array.Empty<Venue>();
                var hadVenues = MapState.Venues.Count > 0;
                MapState.SetVenues(venues);
                MapState.SetLandmarks(request?.NearbyVenues ?? Array.Empty<NearbyVenue>(), request?.Context.Location ?? _profile.EffectiveLocation);

                // A plan with places in it is exactly when the map becomes worth showing.
                if (!_showMap && venues.Count > 0 && !hadVenues)
                {
                    _showMap = true;
                }

                StateHasChanged();
                await ScrollToBottomAsync();
            }
        }

        /// <summary>
        /// Persists a message without holding up the conversation. The transcript has already been
        /// updated, so a slow or failed database write is invisible to the traveler except in the log.
        /// </summary>
        private void QueueSave(
            string chatMessageId,
            string role,
            string content,
            IReadOnlyList<QuickAction>? quickActions,
            ProposedItinerary? itinerary = null)
        {
            _ = SaveInBackgroundAsync(chatMessageId, role, content, quickActions, itinerary);
        }

        private async Task SaveInBackgroundAsync(
            string chatMessageId,
            string role,
            string content,
            IReadOnlyList<QuickAction>? quickActions,
            ProposedItinerary? itinerary)
        {
            try
            {
                var entity = ChatMessageCodec.ToEntity(
                    role,
                    content,
                    DateTime.Now,
                    quickActions,
                    RichContent.FromItinerary(itinerary));

                var saved = await History.SaveMessageAsync(entity);

                // Only now can the message be bookmarked, re-opened, or re-labelled by day.
                ChatState.AttachStoredId(chatMessageId, saved.Id);
            }
            catch (Exception ex)
            {
                // The traveler's answer stays on screen; the history is what fails.
                Logger.LogError(ex, "Could not save {Role} message to chat history", role);
            }
        }

        /// <summary>
        /// One tap on a chip under an agent message. "Show on Map" pans the map with no LLM call; the
        /// other two act on the payload exactly as if the traveler had typed it.
        /// </summary>
        private async Task HandleQuickAction(QuickAction action)
        {
            if (ChatState.IsBusy)
            {
                return;
            }

            if (action.ActionType == QuickActionType.Navigate)
            {
                Activity.RecordActivity();
                await PingHubAsync();

                _showMap = true;
                MapState.SetFocus(action.Payload);
                StateHasChanged();
                return;
            }

            await SendAsync(action.Payload);
        }

        /// <summary>Splits the reply into small beats so the reveal keeps the conversational feel.</summary>
        private static IEnumerable<string> RevealChunks(string text)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\S+\s*");

            for (var index = 0; index < matches.Count; index += 3)
            {
                var chunk = new System.Text.StringBuilder();

                for (var step = 0; step < 3 && index + step < matches.Count; step++)
                {
                    chunk.Append(matches[index + step].Value);
                }

                yield return chunk.ToString();
            }
        }

        private async Task<TravelRequest> BuildRequestAsync(string query)
        {
            var location = _profile.EffectiveLocation;

            // The position wins over the city name when there is one: it makes the weather describe
            // where they are standing and the venue list the places actually around them.
            var at = _profile.Coordinates;
            var context = await ContextProvider.GetCurrentAsync(location, at);

            var nearby = await NearbyVenueStore.GetAsync(context.Location, at);
            var rulebook = _rulebook ?? await RulebookStore.GetAsync(_profile.Destination) ?? new DestinationRulebook
            {
                Destination = _profile.Destination,
                Rules = Array.Empty<SafeguardRule>(),
            };

            return new TravelRequest(query, _profile, context, nearby, rulebook);
        }

        private Task PingHubAsync() => _hubConnection is null || _hubConnection.State != HubConnectionState.Connected
            ? Task.CompletedTask
            : _hubConnection.InvokeAsync("Ping");

        private Task ScrollToBottomAsync()
        {
            try
            {
                return JSRuntime.InvokeVoidAsync("scrollChatToBottom", _messagesContainer).AsTask();
            }
            catch (JSException)
            {
                // JS may be unavailable during prerender; scrolling is best-effort.
                return Task.CompletedTask;
            }
            catch (ObjectDisposedException)
            {
                return Task.CompletedTask;
            }
        }

        public async ValueTask DisposeAsync()
        {
            ChatState.Changed -= OnStateChanged;
            Speech.UtteranceFinal -= OnUtteranceFinalAsync;
            Speech.ListeningStopped -= OnListeningStoppedAsync;
            Speech.ListeningFailed -= OnListeningFailedAsync;
            Speech.SpeakingFinished -= OnSpeakingFinishedAsync;

            await Speech.DisposeAsync();

            if (_hubConnection is not null)
            {
                await _hubConnection.DisposeAsync();
            }

            if (_welcomeTask is not null)
            {
                try
                {
                    await _welcomeTask;
                }
                catch
                {
                    // Greeting failures are already handled inside the task.
                }
            }
        }
    }
}