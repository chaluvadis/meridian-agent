# Sample data

These JSON files drive the app out of the box. Edit them to match your own destination.

| File | Purpose |
| --- | --- |
| `profiles/default.json` | The traveler profile the Context and Safeguard agents read (also editable in the Profile page). |
| `travel_context.json` | Fallback "current reality" when live weather is unavailable (no `Weather__ApiKey` or Nominatim/OSRM outage). |
| `nearby_venues.json` | Ground-truth venues near the traveler, fed to the Search Agent. Drop a `nearby_{slug}.json` for a specific location. |
| `rulebooks/{destination}.json` | Safeguard rules for a destination; indexed into Qdrant for vector narrowing. |

All stores write back into this directory at runtime (for example when you save the Profile page),
and mount a volume over `/app/samples` in Docker to persist your edits.
