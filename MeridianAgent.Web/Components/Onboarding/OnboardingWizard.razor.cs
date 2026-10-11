namespace MeridianAgent.Web.Components.Onboarding
{
    public partial class OnboardingWizard
    {
        [Parameter]
        public WizardMode Mode { get; set; } = WizardMode.Onboarding;

        [Inject]
        private IUserProfileService ProfileService { get; set; } = default!;

        [Inject]
        private NavigationManager Navigation { get; set; } = default!;

        [Inject]
        private ISnackbar Snackbar { get; set; } = default!;

        /// <summary>Index of the last step (Review) — both modes run the same six steps.</summary>
        private const int LastStep = 5;

        private readonly TravelerOnboardingDraft _draft = new();
        private readonly bool[] _visited = new bool[6];
        private int _step;
        private bool _nameError;
        private bool _saving;
        private int _autoSaveGeneration;
        private bool _autoSavePending;
        private bool _autoSaveSaving;
        private string _autoSaveState = "idle";

        /// <summary>Quiet period after the last change before the profile writes itself (edit mode).</summary>
        private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromSeconds(1);

        protected override async Task OnInitializedAsync()
        {
            _step = Mode == WizardMode.Onboarding ? 0 : 1;
            MarkVisited();

            if (Mode == WizardMode.Edit)
            {
                var profile = await ProfileService.GetOrCreateAsync();
                var restored = TravelerOnboardingDraft.From(profile);
                CopyInto(restored, _draft);
                _nameError = false;
            }
        }

        /// <summary>
        /// Applies one answer and, in edit mode, schedules a debounced auto-save: one second of quiet
        /// writes the profile and confirms with a subtle "Saved ✓", so there is never a Save button.
        /// </summary>
        private async Task EditAsync(Action change)
        {
            change();

            if (Mode != WizardMode.Edit)
            {
                return;
            }

            _autoSavePending = true;
            _autoSaveState = "pending";

            // Generation guard: only the newest debounce window is allowed to write.
            var generation = ++_autoSaveGeneration;
            StateHasChanged();

            try
            {
                await Task.Delay(AutoSaveDelay);

                if (generation == _autoSaveGeneration)
                {
                    await AutoSaveAsync(generation);
                }
            }
            catch (ObjectDisposedException)
            {
                // The traveler left the page mid-debounce; nothing was pending.
            }
            catch (Exception)
            {
                // AutoSaveAsync reports failures itself; a stray one must not break the circuit.
            }
        }

        private async Task AutoSaveAsync(int generation)
        {
            if (!_autoSavePending || _autoSaveSaving)
            {
                return;
            }

            _autoSavePending = false;
            _autoSaveSaving = true;
            StateHasChanged();

            try
            {
                var existing = await ProfileService.GetProfileAsync();
                var destination = string.IsNullOrWhiteSpace(existing?.Destination)
                    ? "Dubai, UAE"
                    : existing.Destination;

                var profile = _draft.BuildProfile(
                    existing?.Id ?? IUserProfileService.DefaultProfileId,
                    destination,
                    existing?.HomeBase,
                    existing?.AttireNote,
                    onboardingCompleted: existing?.OnboardingCompleted ?? true,
                    pendingWelcome: false);

                await ProfileService.SaveProfileAsync(profile);

                if (generation == _autoSaveGeneration)
                {
                    _autoSaveState = "saved";

                    try
                    {
                        Snackbar.Add("Saved ✓", MudBlazor.Severity.Success, config => config.VisibleStateDuration = 1400);
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }
            catch (Exception)
            {
                _autoSavePending = true;
                _autoSaveState = "error";

                try
                {
                    Snackbar.Add("I couldn't save that — I'll retry on your next change.", MudBlazor.Severity.Warning);
                }
                catch (ObjectDisposedException)
                {
                }
            }
            finally
            {
                _autoSaveSaving = false;
                StateHasChanged();
            }
        }

        /// <summary>Writes any still-debouncing change so a tap on Done never loses an answer.</summary>
        private async Task FlushAutoSaveAsync() => await AutoSaveAsync(_autoSaveGeneration);

        private string AutoSaveLabel => _autoSaveState switch
        {
            "pending" => _autoSaveSaving ? "Saving…" : "Almost saved…",
            "saved" => "All changes saved ✓",
            "error" => "Not saved yet — I'll retry on your next change",
            _ => "Changes save themselves",
        };

        private void MarkVisited()
        {
            _visited[Math.Clamp(_step, 0, _visited.Length - 1)] = true;
        }

        private void GoToStep(int step)
        {
            var target = Math.Clamp(step, 0, LastStep);
            _step = target;
            MarkVisited();
        }

        private void GoBack() => GoToStep(_step - 1);

        private void GoNext()
        {
            if (_step == 1 && !_draft.IsNameProvided)
            {
                _nameError = true;
                return;
            }

            _nameError = false;
            GoToStep(_step + 1);
        }

        private async Task CompleteAsync()
        {
            if (_saving)
            {
                return;
            }

            if (!_draft.IsNameProvided)
            {
                _nameError = true;
                _step = Mode == WizardMode.Onboarding ? 1 : 0;
                MarkVisited();
                return;
            }

            _saving = true;

            try
            {
                if (Mode == WizardMode.Edit)
                {
                    // Auto-save has been keeping up; write anything still debouncing, then finish.
                    await FlushAutoSaveAsync();
                    Snackbar.Add("Profile updated — your recommendations already reflect it.", MudBlazor.Severity.Success);
                    Navigation.NavigateTo("/");
                    return;
                }

                var existing = await ProfileService.GetProfileAsync();
                var destination = string.IsNullOrWhiteSpace(existing?.Destination)
                    ? "Dubai, UAE"
                    : existing.Destination;

                var profile = _draft.BuildProfile(
                    existing?.Id ?? IUserProfileService.DefaultProfileId,
                    destination,
                    existing?.HomeBase,
                    existing?.AttireNote,
                    onboardingCompleted: true,
                    pendingWelcome: Mode == WizardMode.Onboarding);

                await ProfileService.SaveProfileAsync(profile);

                Snackbar.Add(
                    $"Welcome aboard, {profile.DisplayName.Split(' ')[0]} — I'm getting to know you now.",
                    MudBlazor.Severity.Success);

                Navigation.NavigateTo("/", replace: true);
            }
            catch (Exception)
            {
                // Keep the traveler on the review step with their answers intact.
                Snackbar.Add(
                    "I couldn't save your profile just now — please try saving again.",
                    MudBlazor.Severity.Error);
            }
            finally
            {
                _saving = false;
            }
        }

        private string DisplayNameOrFallback => _draft.IsNameProvided ? _draft.DisplayName : "Not shared yet";

        private string BudgetLabel => TravelerOnboardingDraft.BudgetComforts
            .FirstOrDefault(option => option.Value == _draft.BudgetComfort)?.Label ?? _draft.BudgetComfort;

        private static string ListOr(HashSet<string> values) =>
            values.Count == 0 ? "Nothing selected" : string.Join(" · ", values);

        /// <summary>Copies the answers loaded from an existing profile into the bound draft.</summary>
        private static void CopyInto(TravelerOnboardingDraft from, TravelerOnboardingDraft into)
        {
            into.DisplayName = from.DisplayName;
            into.TravelStyle = from.TravelStyle;
            into.EnergyPattern = from.EnergyPattern;
            into.BudgetComfort = from.BudgetComfort;
            into.Mobility = from.Mobility;
            into.TravelGroup = from.TravelGroup;
            into.SpiceTolerance = from.SpiceTolerance;
            into.FirstTimeVisitor = from.FirstTimeVisitor;

            into.DietaryRestrictions.Clear();
            into.DietaryRestrictions.UnionWith(from.DietaryRestrictions);
            into.FavoriteCuisines.Clear();
            into.FavoriteCuisines.UnionWith(from.FavoriteCuisines);
            into.DiningVibe.Clear();
            into.DiningVibe.UnionWith(from.DiningVibe);
            into.CoreInterests.Clear();
            into.CoreInterests.UnionWith(from.CoreInterests);
            into.HardAvoids.Clear();
            into.HardAvoids.UnionWith(from.HardAvoids);
        }
    }
}