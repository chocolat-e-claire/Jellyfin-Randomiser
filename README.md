# Jellyfin Randomizer

Standalone Jellyfin plugin targeting **Jellyfin Server 10.10.7 / target ABI 10.10.7.0 / .NET 8**.
repository link: https://raw.githubusercontent.com/chocolat-e-claire/Jellyfin-Randomiser/main/manifest.json

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
- Responsive standalone Randomizer page.
- Server-selected roulette-ready result presentation.
- Plugin configuration page.
- Unit-test and GitHub Actions scaffolding.

## Non-invasive Web integration

The plugin does **not** patch Jellyfin Web files, index.html, server binaries, database schema, or Jellyfin source.

The Randomizer UI is exposed as an authenticated standalone page at:

`/Randomizer/Page`

That page uses the plugin's server-side APIs and the current authenticated Jellyfin user. It does not intercept or rewrite `/web` responses.

The repository still contains the earlier embedded `randomizer.js` and `randomizer.css` resources for future Web integration work, but the current safe page does not rely on server-side Web response interception.

## Security

All selection is performed server-side using the authenticated Jellyfin user. Client-supplied item IDs are re-checked through a user-scoped Jellyfin library query; an ID alone does not grant access.

## Build

Requires .NET 8 SDK and network access to restore Jellyfin 10.10.7 packages.

Commands: `dotnet restore Jellyfin-Randomizer.sln`; `dotnet build Jellyfin-Randomizer.sln --configuration Release`; `dotnet test Jellyfin-Randomizer.sln --configuration Release`.

The CI workflow performs the same restore/build/test sequence.

## Compatibility

Only **Jellyfin 10.10.7** is advertised. Later releases require a separate ABI compatibility review.

## Current development status

The stable `main` branch is the proven server/configuration release. The standalone Randomizer page is being developed on a separate branch and must pass CI and a controlled Jellyfin 10.10.7 test before it is merged or published.

## License

MIT. See LICENSE.
