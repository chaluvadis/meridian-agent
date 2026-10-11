namespace MeridianAgent.Web.Components
{
    public partial class GlancePanel
    {
        [Parameter]
        public bool Visible { get; set; }

        [Parameter]
        public EventCallback<bool> VisibleChanged { get; set; }

        private bool _loaded;
        private List<FavoriteRow> _favorites = new();
        private List<DayRow> _days = new();
        private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
        private string? _itinerary;
        private string _itinerarySubtitle = string.Empty;
        private bool _compiling;
        private bool _copied;

        private string _subtitle => _days.Count == 0
            ? "Nothing saved, no days yet"
            : $"{_days.Count} day{(_days.Count == 1 ? "" : "s")} · {_favorites.Count} saved";

        protected override async Task OnParametersSetAsync()
        {
            // The panel only reaches for the database once it is actually open, so a closed glance
            // panel costs a traveler nothing.
            if (Visible && !_loaded)
            {
                _loaded = true;
                await RefreshAsync();
            }
        }

        protected override void OnInitialized()
        {
            // A bookmark set in the chat lands here immediately rather than on the next open.
            ChatState.FavoritesChanged += OnFavoritesChanged;
        }

        public void Dispose()
        {
            ChatState.FavoritesChanged -= OnFavoritesChanged;
        }

        private async void OnFavoritesChanged()
        {
            if (!Visible)
            {
                return;
            }

            await InvokeAsync(async () => await RefreshAsync());
        }

        private async Task RefreshAsync()
        {
            _favorites = LoadFavorites(await History.GetFavoritesAsync());
            _days = LoadDays(await History.GetSessionsAsync());
            await InvokeAsync(StateHasChanged);
        }

        private List<FavoriteRow> LoadFavorites(IReadOnlyList<ChatMessageEntity> stored)
        {
            return stored
                .Select(message => new FavoriteRow(
                    message.Id,
                    message.Timestamp,
                    DayLabel(message.Session),
                    message.Timestamp.ToString("MMM d, h:mm tt"),
                    Preview(message.Content),
                    message.Content,
                    ChatMessageCodec.ReadRichContent(message)))
                .ToList();
        }

        private List<DayRow> LoadDays(IReadOnlyList<ChatSession> stored)
        {
            return stored
                // A day that was opened but never written to is not a day of history worth listing.
                .Where(session => session.Messages.Count > 0)
                .Select(session => new DayRow(
                    session.Id,
                    session.Date,
                    session.DayNumber,
                    session.Messages.Count,
                    session.Date.ToString("MMMM d"),
                    session.Date == _today))
                .OrderByDescending(day => day.Date)
                .ToList();
        }

        private static string DayLabel(ChatSession? session) =>
            session is null ? "Saved" : $"Day {session.DayNumber} · {session.Date:MMM d}";

        /// <summary>The scannable form: one line, no newlines, capped at 60 characters.</summary>
        private static string Preview(string content)
        {
            var flattened = new string((content ?? string.Empty)
                .Replace("\r\n", " ")
                .Replace('\n', ' ')
                .Where(character => !char.IsControl(character))
                .ToArray())
                .Trim();

            while (flattened.Contains("  ", StringComparison.Ordinal))
            {
                flattened = flattened.Replace("  ", " ", StringComparison.Ordinal);
            }

            return flattened.Length <= 60 ? flattened : string.Concat(flattened.AsSpan(0, 60), "…");
        }

        private async Task OpenAsync(FavoriteRow favorite)
        {
            var parameters = new DialogParameters
            {
                ["Favorite"] = favorite,
            };

            var options = new DialogOptions
            {
                MaxWidth = MaxWidth.Small,
                FullWidth = true,
                CloseButton = true,
            };

            await Dialogs.ShowAsync<GlanceMessageDialog>("Saved answer", parameters, options);
        }

        /// <summary>Reads a day into the chat, then gets out of the way so the traveler can read it.</summary>
        private async Task OpenDayAsync(DayRow day)
        {
            var messages = await History.GetMessagesForSessionAsync(day.SessionId);
            var rendered = messages.Select(ChatMessageCodec.ToDomain).ToList();
            var isHistory = day.Date != _today;

            ChatState.LoadSession(day.Date, rendered, isHistory);
            await HideAsync();
        }

        private Task HideAsync() => VisibleChanged.InvokeAsync(false);

        /// <summary>
        /// Lays the answers saved today out as a single day. Only today's saves are used: an itinerary
        /// spanning last week's souks and yesterday's dinner is not a plan for anything.
        /// </summary>
        private async Task CompileItineraryAsync()
        {
            if (_compiling || _favorites.Count == 0)
            {
                return;
            }

            _compiling = true;
            await Task.Yield();

            try
            {
                var saved = await History.GetFavoritesForCurrentSessionAsync();
                var messages = saved.Select(ChatMessageCodec.ToDomain).ToList();

                if (messages.Count == 0)
                {
                    _itinerary = null;
                    return;
                }

                var profile = await ProfileService.GetProfileAsync(IUserProfileService.DefaultProfileId)
                    ?? UserProfile.Default;

                var itinerary = await Synthesizer.GenerateItineraryAsync(profile, messages);

                _itinerary = string.IsNullOrWhiteSpace(itinerary) ? null : itinerary;
                _itinerarySubtitle = _itinerary is null
                    ? string.Empty
                    : $"{_today.ToString("dddd, MMMM d")} · {messages.Count} saved answer{(messages.Count == 1 ? "" : "s")}";
                _copied = false;
            }
            catch (Exception)
            {
                // The model is having a moment. The saved list is untouched, so trying again is free.
                _itinerary = null;
            }
            finally
            {
                _compiling = false;
            }
        }

        private async Task CopyItineraryAsync()
        {
            if (string.IsNullOrWhiteSpace(_itinerary))
            {
                return;
            }

            try
            {
                await JSRuntime.InvokeAsync<bool>("copyTextToClipboard", _itinerary);
                _copied = true;
            }
            catch (JSException)
            {
                // The browser refused to copy; the text is on screen and can be selected by hand.
            }
        }

        private void DiscardItineraryAsync()
        {
            _itinerary = null;
            _copied = false;
        }
    }
}