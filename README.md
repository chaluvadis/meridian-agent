# The Meridian Agent

A hyper-personalized, privacy-first travel companion. Four agents run on one hosted model —
`anthropic/claude-sonnet-4.6` served through the [Kilo AI Gateway](https://kilo.ai/docs/gateway) —
and cooperate as a pipeline that adapts to the traveler's preferences and the reality around them —
weather, time of day, energy — while a "quiet guardian" agent flags local rules with friendly fixes.

The gateway is a single OpenAI-compatible endpoint that fronts hundreds of models (Anthropic,
Google, xAI, DeepSeek, MiniMax, …) behind one API key, so the whole pipeline is one model and one
config line away from being any of them: switch models by editing `AI__Model`, and swap hosts by
editing `AI__BaseUrl`. There is no GPU, no 4 GB of model weights in RAM, and no first-request cold
start; the trade is an API key (`KILO_API_KEY`).

```
User query ──► Context ──► Search ──► Safeguard ──► Synthesizer ──► user
              (claude-sonnet) (claude-sonnet) (claude-sonnet)  (claude-sonnet)
```

| Agent | Model | Job |
| --- | --- | --- |
| Context | `anthropic/claude-sonnet-4.6` | Reduce the profile to the constraints that matter for *this* query |
| Search | `anthropic/claude-sonnet-4.6` | Propose 1–3 venues that fit the constraints *and* the current reality |
| Safeguard | `anthropic/claude-sonnet-4.6` | Check the plan against the destination rulebook; flag with gentle fixes |
| Synthesizer | `anthropic/claude-sonnet-4.6` | The only voice the user hears; streams the final concierge response |
| JSON repair | `anthropic/claude-sonnet-4.6` | Repairs a reply that came back without usable JSON |

Every agent emits JSON (forced with the chat-completions `response_format`, which the gateway
supports), parsed tamper-proof with the `JsonExtractor` (strips markdown fences, repairs trailing
commas) and an LLM JSON-sanitizer fallback, with one validation-feedback retry per agent. Transport
failures are retried with exponential backoff by `RetryingChatClient` (rate limits and 5xx only — a
rejected key fails fast and says so in the log).

### Vector search is optional

The Kilo Gateway serves chat models only, so there is no embedding model to index the rulebook with
by default. Without one, the safeguard reads the full rulebook — a handful of rules per destination,
so the loss is negligible and the guardrails stay intact. Point `AI__EmbeddingModel` (and
`Qdrant__VectorSize`) at any embedding-capable OpenAI-compatible host to switch the Qdrant vector
search back on. A collection left behind by a different-sized embedding model is rebuilt on the next
indexing run.

## Architecture (Clean Architecture)

```
MeridianAgent/
├── MeridianAgent.Core/            Domain + Application: no external dependencies
│   ├── Domain/                    Entities, enums, agent contracts (records), exceptions
│   └── Application/               TravelQueryOrchestrator, AgentBlackboard, agent/store abstractions, DTOs
├── MeridianAgent.Infrastructure/  Kilo Gateway client (Microsoft.Extensions.AI), the 4 agents, Qdrant
│                                  vector store (optional), Nominatim / OSRM / OpenWeatherMap clients,
│                                  JSON file stores
├── MeridianAgent.Web/             Blazor Web App (Interactive Server) + Minimal API /health
├── MeridianAgent.Tests/           319 xUnit tests
├── samples/                       Profiles, rulebooks, nearby venues, fallback context
├── Dockerfile                     SDK 11.0.100-rc.1 → aspnet 11.0.0-rc.1
├── docker-compose.yml             web + qdrant + osrm + overpass (no local model server)
├── global.json / NuGet.Config     Pinned SDK and .NET 11 preview feed
└── .env                           API keys (never commit)
```

## Prerequisites

- .NET SDK `11.0.100-rc.1` (pinned in `global.json`)
- A Kilo API key from [app.kilo.ai/profile](https://app.kilo.ai/profile), set as `KILO_API_KEY`
  (compose passes it as `AI__ApiKey`). The app refuses to start without one — there is no local
  model to fall back to. List the models your key can use with:
  `curl https://api.kilo.ai/api/gateway/models`.
- Optional: Qdrant on `http://localhost:6333` and an OpenWeatherMap key for live weather.
  Both degrade gracefully — without them the app uses the file-backed rulebook and
  `samples/travel_context.json`.

## Run locally

```bash
export KILO_API_KEY=...
dotnet run --project MeridianAgent.Web
```

Then open the printed URL (default <http://localhost:5247> / https). Pages:

- `/` — the chat. It *is* the app: a context header (greeting, weather, local time), the
  transcript, and a composer that opens with three conversation starters. The map is not a
  destination — it rises as a sheet over the transcript when a plan has venues in it, or from
  the **Nearby** button in the header
- `/onboarding` — first-run wizard
- `/profile/edit` — the same wizard as settings, reachable only from the gear icon in the app bar

`GET /health` returns `Healthy` for Coolify/Docker health checks. It also proves the gateway key
works: a key the gateway rejects or an empty balance returns `Unhealthy` (503), so a bad deploy fails
its own gate instead of answering every traveler query with a fallback. A gateway outage or a 5xx is
reported `Degraded` (200), because that says nothing about the key and the next query may still
succeed. Probes are sent at most once a minute.

## Configuration

All settings come from configuration / environment variables (double underscore maps to nesting):

| Variable | Default | Purpose |
| --- | --- | --- |
| `AI__BaseUrl` | `https://api.kilo.ai/api/gateway` | Gateway base URL; must be https (plain `http://` only for a loopback address, since the key rides in the Authorization header) |
| `AI__ApiKey` | *(empty)* | Kilo API key (export as `KILO_API_KEY`) — required, the app fails fast without it |
| `AI__Model` | `anthropic/claude-sonnet-4.6` | Chat model every agent and the JSON repair fallback use |
| `AI__EmbeddingModel` | *(empty)* | Embeddings for Qdrant; empty = off, since the gateway hosts chat models only |
| `AI__RequestTimeoutSeconds` | `120` | Per-request timeout |
| `AI__RetryCount` | `2` | Retries with exponential backoff for rate limits and 5xx |
| `AI__Temperature` | `0.2` | Sampling temperature when an agent does not choose its own |
| `AI__MaxTokens` | `2048` | Reply length when an agent does not choose its own |
| `Qdrant__Endpoint` | `http://localhost:6333` | Vector DB |
| `Qdrant__VectorSize` | `1536` | Must match `AI__EmbeddingModel` when one is configured |
| `Storage__DataDirectory` | `samples` | Profiles / rulebooks / venues on disk |
| `ConnectionStrings:Meridian` | `Data Source=meridian.db` | SQLite file holding the traveler profile |
| `Proactive__Enabled` | `true` | Master switch for proactive suggestions |
| `Proactive__IntervalHours` | `4` | Hours between proactive runs |
| `Proactive__MinConfidenceScore` | `0.55` | Only suggestions at or above this confidence are spoken |
| `Proactive__CooldownHours` | `2` | Minimum gap before repeating the same suggestion |
| `Proactive__ActiveWithinMinutes` | `30` | Stay quiet when the traveler has been idle |
| `Weather__ApiKey` | *(empty)* | OpenWeatherMap; empty = sample context |
| `Nominatim__BaseUrl` | `https://nominatim.openstreetmap.org` | Geocoding |
| `Osrm__BaseUrl` | `https://router.project-osrm.org` | Routing |

## Proactive suggestions

Besides answering questions, the agent can speak first. Every N hours (see `Proactive__*` above)
the `ProactiveSuggestionScheduler` asks the `ProactiveSuggestionAgent` for ideas that fit the
traveler's current profile, weather, time of day, and nearby venues, has the Synthesizer phrase the
strongest ones, and pushes the result into the chat through a SignalR hub (`/chatHub`) as a normal
assistant message — visibly tagged with a "Suggestion" badge and an amber border so it is never
confused with a reactive answer. Suggestions respect dietary rules and the avoid list, only name
venues from the nearby list, and stay silent when nothing clears the confidence bar.

No model is hardcoded at the call site: each agent takes its model as a constructor argument with a
default (`AiOptions.DefaultModel`), so pointing the pipeline at a different model —
`anthropic/claude-haiku-4.5` for speed and cost, `kilo-auto/efficient` for the gateway's own
difficulty routing — is a one-line change to `AI__Model`, with no rebuild. The same applies to the
whole host: setting `AI__BaseUrl` back to `https://api.openai.com/v1` restores the OpenAI path.

## Onboarding and the traveler profile

First-time travelers are redirected to `/onboarding` by a route guard in `Routes.razor`
(`HasCompletedProfileAsync`): a six-step MudBlazor wizard — welcome, about you, food & dining,
interests, logistics, review — captures display name, travel style, energy pattern, dietary
restrictions, spice tolerance, favorite cuisines, dining vibe, core interests, hard avoids, budget
comfort, mobility, group, and first-time-visitor status. On completion the profile is saved with
`onboarding_completed`, a success snackbar fires, and the chat opens with a welcome message
generated by the Synthesizer Agent from everything just captured (one-time, via `pending_welcome`,
with a templated fallback if the model is unreachable). Returning travelers land straight in chat
and can reopen the same wizard through the settings icon in the app bar (`/profile/edit`).

The wizard's answers are stored as the traveler picked them, and mapped onto the agent-facing
profile fields (`DietaryRules`, `Interests`, `AvoidList`, `Pacing`, `Budget`, `GroupComposition`,
`TravelerProfile`) — so all agents keep reading exactly the same contract as before, and profiles
saved earlier keep working unchanged.

Answers are single-tap: every choice is a large chip. Editing a profile (`/profile/edit`) auto-saves
itself — one second after the last change the profile is written and a subtle "Saved ✓" confirms it,
so there is no Save button anywhere.

## Typography (the Dubai font)

The interface is set in **Dubai**, the free typeface commissioned by the Dubai Executive Council with
Microsoft and designed by Monotype. The three weights used by the UI — 400, 500, 700 — are
self-hosted as `.woff2` in `MeridianAgent.Web/wwwroot/fonts/Dubai/` and declared with `@font-face`
at the top of `wwwroot/app.css`, so there is no third-party request at runtime and the app still
renders offline. The MudBlazor theme (`Components/Theme/MeridianTheme.cs`) sets `Dubai` as the
global family with a system sans fallback if the files are ever missing.

To refresh the files: download the family from the Microsoft Typography page for *Dubai* and copy
`Dubai-Regular.woff2`, `Dubai-Medium.woff2`, `Dubai-Bold.woff2` into `wwwroot/fonts/Dubai/`.

## Profile storage (SQLite)

The traveler profile lives in a local SQLite database managed with Entity Framework Core:
`Infrastructure/Data/AppDbContext.cs` maps the existing `UserProfile` record to a single `Profiles`
table, with string lists stored as JSON columns and the domain enums as readable names. Migrations
are applied automatically on startup, and JSON profiles written by earlier builds are imported into
the database on the first run so nothing is lost. The connection string is
`ConnectionStrings:Meridian` (Docker overrides it to the `/app/data` volume).

## One-tap quick actions in chat

Every reply that proposes something concrete comes back with two or three chips underneath it —
"📍 Show on Map", "📅 Plan This", "🔎 More Options". The Synthesizer returns them as JSON alongside
the message (tolerantly parsed: a broken envelope still yields the message, just without chips), and
tapping one either pans the map in the sheet or replays the payload straight through the pipeline,
so acting on a suggestion never means typing.

## Day-wise history and the glance panel

The transcript is no longer ephemeral. Every message is written to SQLite as it happens — the text,
the quick-action chips, and the places the reply proposed — and the app loads today's day back on
start, so a refresh or a new tab picks up where you left off.

Conversations are grouped into **days**: the first message written on a new calendar day opens a
`ChatSession`, numbered from the day the traveler first talked to the agent. A two-day gap between
conversations shows up honestly as a jump in day number rather than being renumbered away. The
session is keyed per profile and per day by a unique index, so two messages racing across midnight
cannot create two rows for the same day.

The **glance** panel (the clock icon in the app bar) is there so none of this has to be scrolled
through:

- **Saved** — every bookmarked reply, newest first, as a scannable one-liner with its day and time.
  "View" opens the answer in full, together with the places it proposed
- **Timeline** — one row per day of conversation, with its message count. Opening a day puts it in
  the chat and the header says "Reading October 9" with a way back to today

Bookmarks are the heart under an agent's reply: grey when unset, gold when saved, applied
immediately and written behind it so the tap never waits on the database. Only the agent's own words
get a heart — bookmarking your own line would be a note to yourself.

The history service is a singleton like the profile service, and for the same reason: it takes an
`IDbContextFactory` and makes a short-lived context per operation, which keeps it safe across Blazor
Server circuits. Writes happen off the render path, so a slow or failed save costs the history, never
the answer on screen.

The schema arrives as the `ChatHistory` migration, applied automatically at startup. Further changes:

```bash
dotnet ef migrations add <Name> --project MeridianAgent.Infrastructure --startup-project MeridianAgent.Web
```

## Logging

Every LLM call is logged through Serilog with model, prompt size, prompt/completion token counts,
and latency; each pipeline stage is logged at start and completion, and the shared
`AgentBlackboard` records every agent's output for debugging.

## Docker

```bash
cp .env.example .env              # add KILO_API_KEY (required) and Weather key if you have one
docker compose up -d              # builds the web app, starts qdrant + osrm + overpass
open http://localhost:8080/health
```

The stack is `meridian-web` + `qdrant` (vector search, idle until an embedding model is configured) +
`osrm`/`overpass` (routing and venue lookup). There is no local model server: the app talks to the
Kilo AI Gateway over HTTPS, so the only required secret is `KILO_API_KEY`, which compose passes to
the container as `AI__ApiKey`. The app fails fast and loudly at startup when it is missing.

`osrm` and `overpass` each download a UAE region extract and build it on first run (15–30 minutes
together); until they are ready, routing falls back to straight-line distances and venues fall back
to `samples/nearby_venues.json`. Both degrade quietly by design.

### One-shot clean build and deploy

`scripts/deploy-local.sh` tears everything down, rebuilds the image from scratch, redeploys, and
waits for `/health` before reporting status:

```bash
./scripts/deploy-local.sh                    # full clean (deletes volumes, rulebooks re-embed)
./scripts/deploy-local.sh --keep-models      # keep the qdrant-data volume, much faster
./scripts/deploy-local.sh --no-cache -l      # no layer cache + follow app logs
./scripts/deploy-local.sh --dry-run          # print every command, change nothing
```

Other flags: `--no-build`, `-y` (skip the confirmation prompt), `--help`.

## Tests

```bash
dotnet test
```

319 xUnit tests cover the JSON extractor (fences, prose, trailing commas, balanced scanning), the
prompt renderer, each agent's parsing/validation/retry behaviour, the blackboard, and the full
orchestrator pipeline including every graceful-degradation path.
