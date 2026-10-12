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

1. Publish this repository and release the plugin archive described under [Releasing](#releasing).
2. Replace the GitHub placeholders in `manifest.json`, including the release URL, owner, checksum, and timestamp.
3. Host the manifest at the repository root on the default branch.
4. In Jellyfin, open **Dashboard > Plugins > Repositories**, add the raw URL to `manifest.json`, save, and install **elCinema**.
5. Restart Jellyfin.
6. Enable elCinema as a movie metadata downloader in the library settings, then refresh the movie metadata.

The repository URL will have this form:

```text
https://raw.githubusercontent.com/<GITHUB_OWNER>/<GITHUB_REPOSITORY>/main/manifest.json
```

The manifest in this repository is a publishing scaffold until its placeholders point to a real release.

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

1. Set the project version in `src/Jellyfin.Plugin.ElCinema/Jellyfin.Plugin.ElCinema.csproj`.
2. Publish the project and zip the contents of `artifacts/publish` so the plugin DLL and its dependencies are at the archive root.
3. Upload the archive to a GitHub release whose tag matches the manifest URL, for example `v5.0.0`.
4. Calculate the archive's MD5 checksum. Jellyfin's plugin repository manifest uses a 32-character hexadecimal checksum:

```powershell
(Get-FileHash .\Jellyfin.Plugin.ElCinema_5.0.0.0.zip -Algorithm MD5).Hash.ToLowerInvariant()
```

5. Update the corresponding version entry in `manifest.json` with the actual release URL, checksum, changelog, and UTC timestamp.
6. Commit and publish the updated manifest.

## Rate Limiting

Requests to elCinema are serialized and paced. Movie and person details are cached in memory; HTTP 429 responses trigger a cooldown using `Retry-After` when provided, or exponential backoff otherwise. The cache is process-local and is cleared when Jellyfin restarts.

## License

See [LICENSE](LICENSE) if present in the source repository. Ensure a compatible license is included before publishing the plugin publicly.
