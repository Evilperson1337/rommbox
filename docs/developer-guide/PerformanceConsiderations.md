# Performance Considerations

This document summarizes performance-related decisions in the plugin and highlights areas that can become bottlenecks.

## Table of Contents

- [API Pagination](#api-pagination)
- [Match Indexing](#match-indexing)
- [Background Work and UI Responsiveness](#background-work-and-ui-responsiveness)
- [File I/O and Media Downloads](#file-io-and-media-downloads)
- [Caching Strategies](#caching-strategies)

## API Pagination

RomM catalogs can be large. [`ImportService`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:26) paginates ROM listings with a fixed `PageSize = 50` to avoid memory spikes.

**Why:** Avoids loading the entire catalog into memory and keeps UI progress reporting responsive.

## Match Indexing

Before importing, the service builds a match index of LaunchBox games to keep duplicate detection near O(1) per ROM.

See [`ImportService.BuildMatchIndex`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:121).

## Background Work and UI Responsiveness

- Downloads, extraction, and install tasks run on background threads (`Task.Run`).
- UI updates are dispatched back to the WPF thread.

Relevant code:

- [`ArchiveService`](src/RomM.LaunchBoxPlugin/Services/ArchiveService.cs:18)
- [`RommMultiMenuItem`](src/RomM.LaunchBoxPlugin/Plugin/Adapters/GameMenu/RommMultiMenuItem.cs:21)

## File I/O and Media Downloads

Media downloads and save files are written directly to disk. This can be I/O heavy on large imports.

See [`ImportService.DownloadMediaAsync`](src/RomM.LaunchBoxPlugin/Services/ImportService.cs:2186).

**Considerations:**

- Use faster storage for LaunchBox media folders.
- Avoid large batch imports when running on slow HDDs.

## Caching Strategies

`PlatformMappingService` caches RomM platform lists for 30 seconds to avoid repeated API calls during UI navigation.

See [`PlatformMappingService.GetPlatformsCachedAsync`](src/RomM.LaunchBoxPlugin/Services/PlatformMappingService.cs:137).

Next: see [Security Model](SecurityModel.md).
