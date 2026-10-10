# The Meridian Agent

A hyper-personalized, privacy-first travel companion. Four small language models (SLMs) run on your
machine via [Ollama](https://ollama.com) and cooperate as a pipeline that adapts to the traveler's
preferences and the reality around them — weather, time of day, energy — while a "quiet guardian"
agent flags local rules with friendly fixes.

```
User query ──► Context ──► Search ──► Safeguard ──► Synthesizer ──► user
(qwen2.5:1.5b)   (qwen2.5:3b)   (qwen2.5:3b)    (llama3.1:8b)
```

| Agent | Model | Job |
| --- | --- | --- |
| Context | `qwen2.5:1.5b` | Reduce the profile to the constraints that matter for *this* query |
| Search | `qwen2.5:3b` | Propose 1–3 venues that fit the constraints *and* the current reality |
| Safeguard | `qwen2.5:3b` | Check the plan against the destination rulebook; flag with gentle fixes |
| Synthesizer | `llama3.1:8b` | The only voice the user hears; streams the final concierge response |

Every agent emits JSON (forced with Ollama's `format: "json"`), parsed tamper-proof with the
`JsonExtractor` (strips markdown fences, repairs trailing commas) and an LLM JSON-sanitizer
fallback, with one validation-feedback retry per agent.

## Architecture (Clean Architecture)

```
MeridianAgent/
├── MeridianAgent.Core/            Domain + Application: no external dependencies
│   ├── Domain/                    Entities, enums, agent contracts (records), exceptions
│   └── Application/               TravelQueryOrchestrator, AgentBlackboard, agent/store abstractions, DTOs
├── MeridianAgent.Infrastructure/  Ollama client, the 4 agents, Qdrant vector store,
│                                  Nominatim / OSRM / OpenWeatherMap clients, JSON file stores
├── MeridianAgent.Web/             Blazor Web App (Interactive Server) + Minimal API /health
├── MeridianAgent.Tests/           54 xUnit tests
├── samples/                       Profiles, rulebooks, nearby venues, fallback context
├── Dockerfile                     SDK 11.0.100-rc.1 → aspnet 11.0.0-rc.1
├── docker-compose.yml             web + ollama + qdrant (+ one-shot model puller)
├── global.json / NuGet.Config     Pinned SDK and .NET 11 preview feed
└── .env                           API keys (never commit)
```

## Prerequisites

- .NET SDK `11.0.100-rc.1` (pinned in `global.json`)
- [Ollama](https://ollama.com) with the four models pulled:

```bash
ollama pull qwen2.5:1.5b
ollama pull qwen2.5:3b
ollama pull llama3.1:8b
ollama pull nomic-embed-text   # only for Qdrant vector narrowing of the rulebook
```

- Optional: Qdrant on `http://localhost:6333` and an OpenWeatherMap key for live weather.
  Both degrade gracefully — without them the app uses the file-backed rulebook and
  `samples/travel_context.json`.

## Run locally

```bash
dotnet run --project MeridianAgent.Web
```

Then open the printed URL (default <http://localhost:5247> / https). Pages:

- `/chat` — streaming chat; toggle the Leaflet map to see the proposed venues
- `/map` — full-page map of the current plan
- `/profile` — edit traveler preferences (saved to `samples/profiles/default.json`)

`GET /health` returns `Healthy` for Coolify/Docker health checks.

## Configuration

All settings come from configuration / environment variables (double underscore maps to nesting):

| Variable | Default | Purpose |
| --- | --- | --- |
| `Ollama__BaseUrl` | `http://localhost:11434` | Local Ollama host |
| `Ollama__RequestTimeoutSeconds` | `300` | Per-request timeout |
| `Ollama__RetryCount` | `2` | Polly retries with exponential backoff |
| `Ollama__EmbeddingModel` | `nomic-embed-text` | Embeddings for Qdrant |
| `Ollama__SanitizerModel` | `qwen2.5:1.5b` | JSON sanitizer fallback model |
| `Qdrant__Endpoint` | `http://localhost:6333` | Vector DB |
| `Storage__DataDirectory` | `samples` | Profiles / rulebooks / venues on disk |
| `Weather__ApiKey` | *(empty)* | OpenWeatherMap; empty = sample context |
| `Nominatim__BaseUrl` | `https://nominatim.openstreetmap.org` | Geocoding |
| `Osrm__BaseUrl` | `https://router.project-osrm.org` | Routing |

No model is hardcoded at the call site: each agent takes its model tag as a constructor argument
with a default, so swapping `llama3.1:8b` for another local model is a one-line change
(`SynthesizerAgent.DefaultModel`).

## Logging

Every LLM call is logged through Serilog with model, prompt size, prompt/completion token counts,
and latency; each pipeline stage is logged at start and completion, and the shared
`AgentBlackboard` records every agent's output for debugging.

## Docker

```bash
cp .env.example .env      # add your Weather__ApiKey if you have one
docker compose up -d      # builds the web app, starts ollama + qdrant, pulls models once
open http://localhost:8080/health
```

The `ollama-models` service pulls the four models on first run (watch it with
`docker compose logs -f ollama-models`); afterwards it stays stopped while `ollama` keeps the
models in the `ollama-models` volume.

## Tests

```bash
dotnet test
```

54 xUnit tests cover the JSON extractor (fences, prose, trailing commas, balanced scanning), the
prompt renderer, each agent's parsing/validation/retry behaviour, the blackboard, and the full
orchestrator pipeline including every graceful-degradation path.
