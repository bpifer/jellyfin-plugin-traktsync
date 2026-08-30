# Jellyfin Trakt Sync

One-way sync of watched movies and episodes from Jellyfin to [Trakt.tv](https://trakt.tv).

Because it runs inside the server, it reads watched state through Jellyfin's internal
APIs — **no Jellyfin API key is required** anywhere in the configuration.

## Features

- Pushes watched **movies** and **episodes** to Trakt, preserving each item's original
  watch timestamp from Jellyfin rather than stamping everything with the sync time.
- **De-duplicates against your Trakt watch history**, so each run submits only what is
  genuinely new.
- **Split-show overrides** for series that Jellyfin and TVDB treat as one continuous show
  but Trakt lists as several separate entries, with a per-season offset so episodes land
  in the right place.
- **Library exclusion** for content that does not exist on Trakt — a YouTube or home
  video library, for example.
- **Dry run** mode that logs exactly what would be sent without writing anything.
- Automatic OAuth token refresh, performed only within 7 days of expiry so single-use
  refresh tokens are not consumed on every run.

## Safe to run against an existing Trakt account

The plugin only ever adds to Trakt; it never removes or overwrites anything. Because it
submits each item's real watch timestamp, Trakt recognises entries it already holds and
does not create duplicate history.

If you would like to confirm that for yourself before it writes anything, enable **Dry
run** and trigger the task manually — it logs the full set of items it would submit.

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
| Jellyfin user ID | **Required.** See below |
| Excluded library ID | Optional. Accepts either a library id or a folder id |
| Shows per request | How many shows are submitted per Trakt call. Default `100` |
| Dry run | Log what would be sent, write nothing |

The plugin will **not** guess which user to sync, and does not fall back to an arbitrary
account if the field is left blank. Picking the wrong user would push someone else's watch
history into your Trakt profile, which cannot be undone from Jellyfin. If no user is set,
it logs a warning and stops.

The sync runs daily at 07:00 by default. Adjust or trigger it from
**Dashboard → Scheduled Tasks → Trakt**, and read its output in the Jellyfin log.

## Building

Requires the .NET 9 SDK, since Jellyfin 10.11 targets `net9.0`:

```bash
dotnet build Jellyfin.Plugin.TraktSync/Jellyfin.Plugin.TraktSync.csproj -c Release -o out
```

Or with no local toolchain:

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet build Jellyfin.Plugin.TraktSync/Jellyfin.Plugin.TraktSync.csproj -c Release -o /src/out
```

To install a local build, copy `out/Jellyfin.Plugin.TraktSync.dll` together with a
`meta.json` into `/var/lib/jellyfin/plugins/Trakt Sync_<version>/` and restart the server.

## Compatibility

Built against **Jellyfin 10.11.x** (`Jellyfin.Controller` 10.11.11, `net9.0`).

The `Jellyfin.Controller` package version must match your server version, otherwise the
plugin is reported as *NotSupported* and will not load.

## License

GPL-3.0. See [LICENSE](LICENSE).
