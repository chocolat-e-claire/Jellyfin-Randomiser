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

The plugin does **not** patch Jellyfin Web files, server binaries, database schema, or Jellyfin source.

The plugin has two UI surfaces:

- An authenticated standalone page at `/Randomizer/Page`.
- A Jellyfin Web enhancement registered through the separate **File Transformation** plugin.

The Web integration registers a transformation for `index.html`; the transformation only appends the Randomizer loader tags to the response in memory. It never edits the installed Jellyfin Web files.

The Web enhancement uses Jellyfin Web's `window.ApiClient` for plugin API calls and is guarded against duplicate injection during SPA navigation.

File Transformation is an optional integration dependency. When it is unavailable, Randomizer logs a warning and Jellyfin continues starting normally.

## Security

All selection is performed server-side using the authenticated Jellyfin user. Client-supplied item IDs are re-checked through a user-scoped Jellyfin library query; an ID alone does not grant access.

## Build

Requires .NET 8 SDK and network access to restore Jellyfin 10.10.7 packages.

Commands: `dotnet restore Jellyfin-Randomizer.sln`; `dotnet build Jellyfin-Randomizer.sln --configuration Release`; `dotnet test Jellyfin-Randomizer.sln --configuration Release`.

The CI workflow performs the same restore/build/test sequence.

## Compatibility

Only **Jellyfin 10.10.7** is advertised. Later releases require a separate ABI compatibility review.

## Validation

The feature branch is validated in disposable GitHub Actions environments using the real Jellyfin Server 10.10.7 container.

The integration smoke test installs File Transformation 2.5.9.0 and Randomizer into a fresh Jellyfin configuration, verifies Web transformation registration and injected assets, creates synthetic movie/TV fixtures, configures a restricted Jellyfin user, and exercises the Randomizer API for library visibility, search limits, movies, shows, single-show episodes, multi-show episode strategies, watched/unwatched filters, history avoidance, permission enforcement, and normal item details access.

A second CI job starts Jellyfin 10.10.7 with Randomizer but **without** File Transformation and verifies that the optional integration failure is non-fatal.

The stable `main` branch remains the proven server/configuration release. The feature branch must pass these isolated tests before it is merged or published.

## License

MIT. See LICENSE.
