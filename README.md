# Jellyfin Randomizer

Jellyfin plugin targeting **Jellyfin Server 10.10.7 / target ABI 10.10.7.0 / .NET 8**.

## Features

- Random movie.
- Random TV show.
- Random episode from one selected show, multiple selected shows, or an entire accessible TV library.
- Equal probability per show or per eligible episode.
- All / watched / unwatched filters.
- Server-side search with bounded result pages.
- Server-side permission filtering for explicit IDs.
- Optional per-user recent-result avoidance (in-memory).
- Normal Jellyfin details and native playback flows.
- Authenticated standalone Randomizer page.
- Jellyfin Web integration through the File Transformation plugin.
- Plugin configuration page.
- Unit tests and reproducible GitHub Actions acceptance checks.

## Non-invasive Web integration

The plugin does **not** patch Jellyfin Web files, server binaries, database schema, or Jellyfin source.

The plugin provides two UI surfaces:

- An authenticated standalone page at `/Randomizer/Page`.
- A Jellyfin Web enhancement registered through the separate **File Transformation** plugin.

The Web integration registers a transformation for `index.html`. The transformation inserts the Randomizer CSS/JavaScript loader tags into the response in memory; it never edits the installed Jellyfin Web files.

The injected Web script waits for Jellyfin Web's `window.ApiClient` before initializing, uses the authenticated Jellyfin API for Randomizer requests, and guards against duplicate button injection during SPA navigation.

The Randomizer Play action follows Jellyfin Web's native Details Play control rather than depending on a private or global playback-manager API.

File Transformation is an optional integration dependency. When it is unavailable, Randomizer logs a controlled warning and Jellyfin continues starting normally.

## Security

All selection is performed server-side using the authenticated Jellyfin user. Client-supplied item IDs are re-checked through a user-scoped Jellyfin library query; an ID alone does not grant access.

## Build

Requires .NET 8 SDK and network access to restore Jellyfin 10.10.7 packages.

Commands:

- `dotnet restore Jellyfin-Randomizer.sln`
- `dotnet build Jellyfin-Randomizer.sln --configuration Release`
- `dotnet test Jellyfin-Randomizer.sln --configuration Release`

The CI workflow performs the same restore/build/test sequence and verifies the packaged plugin structure.

## Compatibility

Only **Jellyfin 10.10.7** is advertised. Later releases require a separate ABI compatibility review.

## Validation

The feature branch is validated only in disposable GitHub Actions environments using the real Jellyfin Server 10.10.7 container; production NAS infrastructure is not used for development or testing.

The integration acceptance workflow installs File Transformation 2.5.9.0 and Randomizer into a fresh Jellyfin configuration, verifies Web transformation registration and injected assets, compares Jellyfin file hashes against a pristine image, creates synthetic movie/TV fixtures, configures a restricted Jellyfin user, and exercises the Randomizer API for library visibility, search limits, movies, shows, single-show episodes, multi-show episode strategies, watched/unwatched filters, history avoidance, permission enforcement, inaccessible IDs, empty/no-eligible results, and normal Jellyfin details access.

The same workflow runs a separate Jellyfin 10.10.7 leg without File Transformation and verifies that the optional integration failure is non-fatal.

A Playwright/Chromium browser leg verifies authenticated Web startup, the standalone Randomizer page, Movies/TV Randomize buttons, modal controls, restricted library visibility, search, native Jellyfin Details navigation and native playback flow, and repeated Movies → TV → Movies → TV SPA navigation without duplicate injection.

The feature branch remains unreleased until the full isolated acceptance suite passes. The stable `main` branch remains the proven release line.

## License

MIT. See LICENSE.
