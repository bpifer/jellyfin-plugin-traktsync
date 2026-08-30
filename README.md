# Jellyfin Trakt Sync

One-way sync of watched movies and episodes from Jellyfin to [Trakt.tv](https://trakt.tv).

Ported from a standalone Python script to a native Jellyfin plugin. Because it runs
inside the server it uses Jellyfin's internal APIs directly, so **no Jellyfin API key
is required** anywhere in the configuration.

## Features

- Pushes watched **movies** and **episodes** to Trakt, preserving the original
  `watched_at` timestamp from Jellyfin.
- **De-duplicates against Trakt watch history**, so only genuinely new items are sent.
- **Split-show overrides** for series that Jellyfin/TVDB treat as one continuous show
  but Trakt splits into several (for example *The Great British Bake Off*).
- **Library exclusion**, for content that does not exist on Trakt (a YouTube library,
  for instance).
- **Dry run** mode that logs exactly what would be sent without writing to Trakt.
- Automatic OAuth token refresh, only within 7 days of expiry so single-use refresh
  tokens are not burned on every run.

## Two bugs worth knowing about

The original script re-sent the entire watched library every night. Two causes, both
fixed here and commented in the source so they do not regress:

1. **`/sync/watched/shows` returns no season or episode data.** Every episode set came
   back empty, so the de-duplication check could never match. Per-episode state is now
   built from paginated `/sync/history/episodes` instead.
2. **Pagination was ignored.** Trakt defaults to 100 items per page, so only the first
   page of watched shows was ever read.

This is safe to run against an existing Trakt account: because the original
`watched_at` timestamp is sent, Trakt de-duplicates server side and does not create
duplicate history entries.

## Installation

Add this repository under **Dashboard → Plugins → Repositories**:

```
https://raw.githubusercontent.com/bpifer/jellyfin-plugin-traktsync/main/manifest.json
```

Then install **Trakt Sync** from the catalogue and restart Jellyfin.

## Configuration

**Dashboard → Plugins → Trakt Sync**

| Setting | Notes |
|---|---|
| Client ID / secret | From your [Trakt API application](https://trakt.tv/oauth/applications) |
| Access / refresh token | Obtained via the Trakt OAuth flow |
| Token expires at | Unix timestamp; set to `0` to force a refresh on the next run |
| Jellyfin user ID | **Required.** No fallback by design, see below |
| Excluded library ID | Optional |
| Dry run | Log what would be sent, write nothing |

The plugin will **not** guess which user to sync. Falling back to an arbitrary account
would push someone else's watch history into the configured Trakt profile, which cannot
be undone from Jellyfin. If the user ID is unset it logs a warning and stops.

Runs daily at 07:00 by default: **Dashboard → Scheduled Tasks → Trakt**.

## Building

Requires the .NET 9 SDK (Jellyfin 10.11 targets `net9.0`):

```bash
dotnet build Jellyfin.Plugin.TraktSync/Jellyfin.Plugin.TraktSync.csproj -c Release -o out
```

Or without installing anything locally:

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet build Jellyfin.Plugin.TraktSync/Jellyfin.Plugin.TraktSync.csproj -c Release -o /src/out
```

Copy `out/Jellyfin.Plugin.TraktSync.dll` plus a `meta.json` into
`/var/lib/jellyfin/plugins/Trakt Sync_<version>/` and restart the server.

## Compatibility

Built against **Jellyfin 10.11.x** (`Jellyfin.Controller` 10.11.11, `net9.0`).
The package reference must match your server version or the plugin shows as
*NotSupported*.

## License

GPL-3.0.
