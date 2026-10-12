# Jellyfin elCinema Plugin

A Jellyfin metadata and image plugin for Egyptian and Arabic movies indexed by [elCinema](https://elcinema.com/). It retrieves movie information, artwork, and linked people directly from elCinema.

## Features

- Arabic and transliterated movie titles
- Production year, synopsis, community rating, and genres
- Director, writer, and cast credits
- Movie posters and person profile images
- elCinema IDs and clickable elCinema movie links
- Shared request pacing, 429 backoff, and in-memory caching to reduce load on elCinema

## Compatibility

- Jellyfin Server `10.11.9`
- Plugin target ABI `10.11.9.0`
- .NET `9`

The plugin is built against Jellyfin `10.11.9` APIs. Use a release built for the ABI matching your Jellyfin server.

## Install From a Plugin Repository

Add the plugin repository in **Dashboard > Plugins > Repositories** using this manifest URL:

`https://raw.githubusercontent.com/mostafaabdelrahman/jellyfin-plugin-elcinema/main/manifest.json`

After a release has been published, install **elCinema**, restart Jellyfin, enable it as a movie metadata downloader in the library settings, and refresh movie metadata.

## Latest Release

<!-- RELEASE_INFO_START -->
No release has been published yet. See [Releasing](#releasing).

**Plugin repository manifest:** https://raw.githubusercontent.com/mostafaabdelrahman/jellyfin-plugin-elcinema/main/manifest.json
<!-- RELEASE_INFO_END -->

## Manual Installation

Build or download the release archive, extract its contents into a dedicated folder under Jellyfin's `plugins` directory, and restart Jellyfin. The plugin folder must include `Jellyfin.Plugin.ElCinema.dll`, its `.deps.json`, and required dependencies such as `AngleSharp.dll`.

For Docker installations, use the server's configured plugin data directory, commonly mounted under `/config/plugins`.

## Configuration

The plugin configuration page includes:

- **Prefer Arabic title**: use the Arabic title as the movie name; otherwise use the transliterated title when available.
- **Import actors**, **Import directors**, and **Import writers**: enable or disable the corresponding movie credits.
- **User-Agent**: HTTP User-Agent used for elCinema requests.

## Build

Install the .NET 9 SDK, then run:

```powershell
dotnet build src/Jellyfin.Plugin.ElCinema/Jellyfin.Plugin.ElCinema.csproj -c Release
```

To create files suitable for packaging:

```powershell
dotnet publish src/Jellyfin.Plugin.ElCinema/Jellyfin.Plugin.ElCinema.csproj -c Release -o artifacts/publish
```

## Releasing

The release is automated by [the GitHub Actions workflow](.github/workflows/release.yml). To publish a new version:

```powershell
# Update <Version> in the project file, commit to main, and push the matching tag.
git tag v1.0.0.0 -m "Release v1.0.0.0"
git push origin v1.0.0.0
```

The four-part `vX.Y.Z.W` tag must exactly match the project version. The workflow publishes the plugin, creates a release ZIP with the published files at its root, creates a GitHub Release with generated notes, calculates the MD5 checksum, and updates `manifest.json` plus the latest-release section above on `main`.

## Rate Limiting

Requests to elCinema are serialized and paced. Movie and person details are cached in memory; HTTP 429 responses trigger a cooldown using `Retry-After` when provided, or exponential backoff otherwise. The cache is process-local and is cleared when Jellyfin restarts.

## License

See [LICENSE](LICENSE) if present in the source repository. Ensure a compatible license is included before publishing the plugin publicly.
