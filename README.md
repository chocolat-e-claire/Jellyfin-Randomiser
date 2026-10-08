# Jellyfin Randomizer

Standalone Jellyfin plugin targeting **Jellyfin Server 10.10.7 / target ABI 10.10.7.0 / .NET 8**.

## Features

- Random movie.
- Random TV show.
- Random episode from one selected show, multiple selected shows, or an entire accessible TV library.
- Equal probability per show or per eligible episode.
- All / watched / unwatched filters.
- Server-side search with bounded result pages.
- Server-side permission filtering for explicit IDs.
- Optional per-user recent-result avoidance (in-memory).
- Normal Jellyfin details/playback routes.
- Responsive keyboard-accessible modal.
- Server-selected roulette presentation.
- Plugin configuration page.
- Unit-test and GitHub Actions scaffolding.

## Non-invasive Web integration

The plugin does **not** patch Jellyfin Web files, index.html, server binaries, database schema, or Jellyfin source. JavaScript and CSS are embedded in the plugin assembly and injected into /web responses in the ASP.NET Core pipeline.

No modified Jellyfin Web build and no core-file replacement is required.

## Security

All selection is performed server-side using the authenticated Jellyfin user. Client-supplied item IDs are re-checked through a user-scoped Jellyfin library query; an ID alone does not grant access.

## Build

Requires .NET 8 SDK and network access to restore Jellyfin 10.10.7 packages.

Commands: dotnet restore Jellyfin-Randomizer.sln; dotnet build Jellyfin-Randomizer.sln --configuration Release; dotnet test Jellyfin-Randomizer.sln --configuration Release.

The CI workflow performs the same restore/build/test sequence.

**This session has not run the .NET build or a live Jellyfin 10.10.7 acceptance test.** The branch is not represented as live-tested.

## Install

Build Release, then copy Jellyfin.Plugin.Randomizer.dll and meta.json into a plugin folder such as <jellyfin-data>/plugins/Jellyfin-Randomizer_0.1.0.0/. Restart Jellyfin and hard-refresh Jellyfin Web.

## Compatibility

Only **Jellyfin 10.10.7** is advertised. Later releases require a separate ABI compatibility review.

## License

MIT. See LICENSE.