# Plan: Jellyfin Native elCinema Scraper Plugin (.NET / C#)

Implement a native Jellyfin metadata and image provider plugin in C# (.NET 8) that ports the scraping logic from `write_nfo_file.py` to directly fetch and enrich movie metadata and posters from elcinema.com.

### Architecture Overview

- **Runtime & Target**: .NET 8 (targeting Jellyfin 10.9.x / 10.10.x server APIs).
- **Core Dependencies**: `Jellyfin.Controller`, `Jellyfin.Model`, and `AngleSharp` (for robust CSS selector and HTML DOM parsing matching BeautifulSoup behavior).
- **Key Plugin Components**:
  1. `Plugin.cs`: Entry point inheriting `BasePlugin<PluginConfiguration>`, managing plugin metadata and GUID.
  2. `Configuration/PluginConfiguration.cs`: User options (e.g., preferred primary title language: Arabic vs. Transliterated English, custom User-Agent).
  3. `ExternalId/ElCinemaExternalId.cs`: Registers elCinema as an external provider ID in Jellyfin UI, linking to `https://elcinema.com/work/{id}/` and enabling the Jellyfin "Identify" dialog.
  4. `Services/ElCinemaApiClient.cs`: Service handling HTTP requests with custom headers, rate-limiting, and DOM parsing with AngleSharp.
  5. `Providers/ElCinemaMovieProvider.cs`: Implements `IRemoteMetadataProvider<Movie, MovieInfo>`, handling both search queries (`GetSearchResults`) and full metadata retrieval (`GetMetadata`).
  6. `Providers/ElCinemaImageProvider.cs`: Implements `IRemoteImageProvider`, fetching primary artwork / poster images.

---

### Steps

#### Phase 1: Project Scaffolding and Build Configuration

1. **Initialize .NET Class Library Project**: Create `Jellyfin.Plugin.ElCinema.csproj` targeting `net8.0`, configure package references for `Jellyfin.Controller` and `Jellyfin.Model` (marked with `PrivateAssets="all"` so server dependencies are not bundled), and add `AngleSharp`.
2. **Setup Plugin Entry Point and Configuration**: Implement `Plugin.cs` inheriting `BasePlugin<PluginConfiguration>` with unique GUID and versioning, plus `PluginConfiguration.cs` with options for preferred title language (Arabic or English) and custom User-Agent. (_depends on step 1_)
3. **Register External ID Provider**: Implement `ElCinemaExternalId.cs` implementing `IExternalId`, declaring external ID key `ElCinema`, media type `ExternalIdMediaType.Movie`, and URL format `https://elcinema.com/work/{0}/`. (_depends on step 2_)

#### Phase 2: HTTP Client and Scraping Service (Porting Python Logic)

4. **Implement Scraper Client (`ElCinemaApiClient`)**: Build an encapsulated scraping service injected with `IHttpClientFactory` and `ILogger`. Configure realistic browser User-Agent header (`Mozilla/5.0...`). (_depends on step 1_)
5. **Port Search Logic (`SearchAsync`)**: In `ElCinemaApiClient`, send query to `https://elcinema.com/search/?q={query}`, parse result HTML using AngleSharp, locate work anchors matching `/work/{id}`, extract candidate work IDs and corresponding titles, and return structured search candidates. (_depends on step 4_)
6. **Port Movie Details Parsing (`GetMovieDetailsAsync`)**: In `ElCinemaApiClient`, query `https://elcinema.com/work/{id}/` and extract fields matching `parse_elcinema_movie` in `write_nfo_file.py`:
   - Title in Arabic (`span[dir="rtl"]`) and English transliteration (`span[dir="ltr"]`).
   - Production year via regex `\((\d{4})\)`.
   - Community star rating (`.stars-rating-lg .legend`).
   - Clean plot description (`p` tag, removing `#read-more` anchor).
   - Genres (`li` containing "تصنيف العمل:").
   - Credits: Directors (`ﺇﺧﺮاﺝ:`), Writers (`ﺗﺄﻟﻴﻒ:`), and Cast (`طاقم العمل:`) mapped from `ul.list-separator`.
   - Poster URL (`div.intro-box img[src]`).
   - Stream/trailer link (`a.cta-watch_now[href]`). (_depends on step 4_)

#### Phase 3: Jellyfin Metadata and Image Providers

7. **Implement Movie Metadata Provider (`ElCinemaMovieProvider`)**: Implement `IRemoteMetadataProvider<Movie, MovieInfo>` and `IHasOrder`:
   - `GetSearchResults`: Map `searchInfo.Name` / year to `ElCinemaApiClient.SearchAsync` and return `RemoteSearchResult` entries populated with `ElCinema` provider ID.
   - `GetMetadata`: Lookup `ElCinema` provider ID (or execute search fallback), fetch details via `ElCinemaApiClient`, populate Jellyfin `Movie` instance (Title, OriginalTitle, Overview, ProductionYear, CommunityRating, Genres) and `PersonInfo` list (Directors, Writers, Actors). (_depends on steps 5, 6_)
8. **Implement Remote Image Provider (`ElCinemaImageProvider`)**: Implement `IRemoteImageProvider` for `Movie`:
   - `GetImages`: Read `item.GetProviderId("ElCinema")`, retrieve poster URL, and yield `RemoteImageInfo` of type `ImageType.Primary`.
   - `GetImageResponse`: Stream image payload through `IHttpClientFactory` ensuring appropriate referer/user-agent headers. (_parallel with step 7, depends on step 6_)

#### Phase 4: Packaging and Verification

9. **Build and Assembly Packing**: Configure post-build / publish instructions to output a plugin directory structure ready for Jellyfin's `plugins/` directory or zip distribution. (_depends on steps 7, 8_)
10. **Unit and Integration Testing**: Write automated xUnit tests validating:
    - HTML parsing of sample elCinema HTML fixtures (using the existing movies in workspace: "نقل مخ" and "الهانم بالنيابة عن مين").
    - Search query matching and ID extraction.
    - Movie metadata mapping to Jellyfin `MetadataResult<Movie>`. (_parallel with step 9_)

---

### Relevant Files

- `Jellyfin.Plugin.ElCinema.csproj` — Project definition targeting `net8.0` with `Jellyfin.Controller` and `AngleSharp`.
- `Jellyfin.Plugin.ElCinema/Plugin.cs` — Main plugin class inheriting `BasePlugin<PluginConfiguration>`.
- `Jellyfin.Plugin.ElCinema/Configuration/PluginConfiguration.cs` — Configuration schema (language choice, User-Agent).
- `Jellyfin.Plugin.ElCinema/ExternalId/ElCinemaExternalId.cs` — External ID registration for UI links and identification.
- `Jellyfin.Plugin.ElCinema/Services/ElCinemaApiClient.cs` — Core scraper porting functions `search_elcinema_id` and `parse_elcinema_movie`.
- `Jellyfin.Plugin.ElCinema/Providers/ElCinemaMovieProvider.cs` — Jellyfin metadata provider implementing `IRemoteMetadataProvider<Movie, MovieInfo>`.
- `Jellyfin.Plugin.ElCinema/Providers/ElCinemaImageProvider.cs` — Jellyfin image provider implementing `IRemoteImageProvider`.
- `tests/Jellyfin.Plugin.ElCinema.Tests/ParserTests.cs` — Unit tests verifying DOM parsing against elCinema HTML fixtures.

---

### Verification

1. **Compilation Check**: Run `dotnet build` on the solution to verify zero compiler warnings/errors and proper package resolution.
2. **Parser Unit Tests**: Execute `dotnet test` against mock HTML responses for both test movies (`نقل مخ` ID 2044729 and `الهانم بالنيابة عن مين`) ensuring parsed properties match expected fields in `.nfo` files.
3. **Live Test / Search Verification**: Validate `ElCinemaApiClient.SearchAsync("نقل مخ")` retrieves ID 2044729 and extracts title, year (1989), rating (5.2), directors, writers, cast, and plot.
4. **Jellyfin Plugin Deployment Test**: Deploy compiled `.dll` to Jellyfin's `plugins/ElCinema/` folder, restart Jellyfin, verify plugin loads under _Dashboard > Plugins_, and run "Identify" on a movie using query "نقل مخ".

---

### Decisions & Scope Boundaries

- **Language Priority**: Jellyfin's `Title` will default to the Arabic title while `OriginalTitle` and `SortName` will receive the transliterated English title (preserving the exact behavior from `write_nfo_file.py`). A plugin setting will allow flipping this.
- **Scope**: Initial release focuses on Movies (`IRemoteMetadataProvider<Movie, MovieInfo>`). TV Series (`Series` / `Season` / `Episode`) can be planned as a secondary phase since elCinema's TV episode layout requires additional hierarchy handling.
- **HTML Parsing Engine**: Selected `AngleSharp` over `HtmlAgilityPack` for modern CSS3 selector support (`span[dir="rtl"]`, `.stars-rating-lg .legend`), mirroring BeautifulSoup `soup.find()` / `soup.select()`.

---

<!-- ### Further Considerations

1. **Series Support**: Would you like the initial implementation to support only Movies, or should TV Series scraper structures (`IRemoteMetadataProvider<Series, SeriesInfo>`) be included in the initial design? (Recommendation: Start with Movies to match the existing Python script, then expand to Series).
2. **Fallback / Locale Handling**: Should elCinema English pages (`https://elcinema.com/en/...`) be queried when the user's Jellyfin library language is set to English? (Recommendation: Keep Arabic as default since it contains richer metadata and plot summaries, while populating `OriginalTitle` with the transliteration). -->
