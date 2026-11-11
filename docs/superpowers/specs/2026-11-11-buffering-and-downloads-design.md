# Buffering, Episode Downloads and Upcoming Playlist — Design

## Intent

Let listeners keep hearing their station when the network is unreliable or
absent, and let them see what is coming next.

Each station gets its own "buffer ahead" setting (Off, 10, 15, 30, 60 or 120
minutes). When it is on for a station, the app downloads whole upcoming
episodes of that station to local storage and plays those local files
instead of streaming. The player gets an "Up next" panel, opened by swiping
right, that shows the station's upcoming episodes and whether each one is
ready on this device.

Downloads are deliberately **not** offered for host-paced or aging
stations, and the UI explains why.

## Goals

- Persist a buffer target per station in the app config.
- Fetch the ordered upcoming playlist from the player API (new endpoint).
- Download whole episodes in playlist order until the buffered audio reaches
  the target, one at a time.
- Play a downloaded episode from disk when it is the current episode.
- Show upcoming episodes and per-episode download state in the player.
- Disable downloads, with a visible reason, when the active station is
  host-paced (`HostPaceEnabled`) or aging (`IsAging`).
- Start with no network and play any station whose content is downloaded,
  including switching stations and advancing into the next downloaded
  episode.
- Keep working against older self-hosted player APIs that do not have the
  new endpoint.

## Non-goals

- Starting a station offline that has no downloaded content. It is shown in
  the sidebar but reports that it is not available offline.
- Downloading arbitrary episodes from the Up next panel (no jump-to-episode).
- Downloading on the stream server.
- A server-side duration field or feed re-parsing.
- Background downloads while the app is closed (no mobile background service).
- Download limits by storage size or per-account quotas.

## My calls

- **Minutes mean audio time, not wall-clock time.** Count the current
  episode's *remaining* audio (its duration minus the status playhead) plus
  the full duration of following episodes. Whole episodes are downloaded, so
  the result may overshoot (a 90-minute episode satisfies a 30-minute target
  by itself).
- **The setting is per station.** Each station keeps its own minutes value;
  changing one station never changes another's downloads.
- **Only the active station downloads.** Switching stations stops the
  outgoing station's queue but keeps its downloaded episodes and snapshot.
  Every downloaded station stays playable offline.
- **Off is the default.** A station does not download until the listener
  chooses a target for it.
- **The current episode is downloaded too.** It is used the next time that
  episode is loaded; a download finishing mid-episode does not switch the
  live playback from network to disk.
- **Lowering the target prunes.** Turning the setting down deletes cached
  episodes beyond the new target; Off deletes the station's cache except the
  file currently loaded. The cache is disposable, never the listener's data.
- **Up next is available even when downloads are off.** Visibility is a
  separate feature from buffering.
- **Offline covers every downloaded station.** Each played station gets its
  own snapshot and download folder, so the app can start, show the sidebar,
  switch between downloaded stations, and continue through downloaded
  episodes without the server.
- **Offline completions are not replayed to the server.** On reconnect the
  app syncs the episode and position it is on. Episodes finished while
  offline are not marked listened server-side; the next server-driven choice
  may therefore include one again.

## Constraints

- App: Avalonia 12 / .NET 10, existing `ConfigService`, `MainViewModel`,
  `SoundFlowAudioPlayer` and test conventions.
- Backend: `Ohrbob.Player` controller plus `PlaybackStrategyService`; reuse
  the existing ordering, host-pace and listened-state rules; do not invent a
  second playlist algorithm.
- Both repositories must keep passing `dotnet test`.
- No secrets or user data in logs. Do not delete anything except files the
  download manager created inside its own cache directory.

## Backend: new playlist endpoint

Add to `Ohrbob.Player/Controllers/StreamController.cs`:

```http
GET /stream/playlist?limit=50
X-Stream-Token: <token>
```

Response:

```json
{
  "stationId": "...",
  "hostPaceEnabled": false,
  "isAging": false,
  "episodes": [
    {
      "episodeId": "...",
      "title": "...",
      "description": "...",
      "artist": "...",
      "coverUrl": "...",
      "streamUri": "...",
      "publishedAt": "2026-11-01T10:00:00Z",
      "isCurrent": true
    }
  ]
}
```

- Auth follows the existing `GetProfile()` / `X-Stream-Token` route.
- The active station is resolved exactly like `GET /stream/status`, including
  the fallback to the first allowed station. No `stationId` parameter: the
  player always shows the station it is playing.
- The current episode is resolved exactly like `GET /stream/status`: use
  `LastActiveEpisodeId` only if it belongs to the station and is still
  playable, otherwise let the playback strategy pick. `isCurrent` marks the
  first item. On a fresh profile the list therefore still starts with the
  episode status will show.
- `episodes` starts with that current episode when one exists, then the
  upcoming episodes in playback order. The sequence matches what repeated
  `POST /stream/next` calls would produce, including the existing wrap to the
  first unplayed episode; with `EndlessSpin` it keeps cycling the full
  rotation. It stops at `limit`.
- `description` uses `PlainDescription`.
- `artist` uses the existing feed-title-or-station-name resolution.
- `limit` is clamped to 1..100, default 50.
- If there is no active station or no current episode, return
  `200 { "stationId": ..., "episodes": [] }` rather than an error.
- Host-paced filtering and listened-episode filtering must match
  `GET /stream/status` / `/stream/next`.

Implementation: add `GetUpcomingEpisodesAsync(station, currentEpisodeId,
listener, limit)` to `PlaybackStrategyService`. It returns the sequence above
and `GetNextEpisodeAsync` is refactored to take its first item without
changing `/next` behavior; focused tests pin both paths.

## App: config

Add a per-station map to `ConfigService` / `ConfigData`:

```csharp
public int GetBufferMinutes(string stationId);
public void SetBufferMinutes(string stationId, int minutes);
```

Allowed values are 0 (Off), 10, 15, 30, 60, 120; a missing station or
unknown value reads as 0. These helpers write the same config file as
`Save`, without requiring a reconnect. Do not add a single global value.

## App: playlist data

Add `Models/PlaylistDto.cs` / `PlaylistEpisodeDto.cs` matching the endpoint,
and `OhrbobClient.GetPlaylistAsync(int limit = 50)`. A 404 or an invalid
response means "playlist unavailable" (old server), not a fatal app error.

Add `HostPaceEnabled` to the app's `Station` model; `IsAging` already exists.

Add a `PlaylistItem` observable model used by the UI:

- episode id, title, description, artist, cover URL, stream URI, published at
- `IsCurrent`
- `DownloadState`: `NotDownloaded`, `Queued`, `Downloading`, `Downloaded`,
  `Failed`
- `CanDownload` and a per-station `DownloadDisabledReason`

## App: download manager

New `Services/EpisodeDownloadService.cs`, owned by `MainViewModel`.

- Cache root:
  `%ApplicationData%/Ohrbob/downloads/<stationId>/<episodeId><ext>`.
  Sanitize ids; keep the URL's extension (`.mp3`, `.m4a`, `.ogg`), fall back
  to `.audio`.
- Download in playlist order starting at the current episode, one at a time,
  to `<file>.part`, then rename on success. Delete `.part` on cancel/failure.
- Budget = current episode's duration minus the status playhead, plus the
  full duration of following episodes. Stop once the budget reaches that
  station's minutes value. Duration comes from SoundFlow's existing
  `SoundMetadataReader.Read(path, new ReadOptions { ReadTags = false,
  ReadAlbumArt = false, DurationAccuracy = FastEstimate })`. If metadata
  cannot be read, treat the episode as 0 duration but keep it as downloaded;
  the next episode will then be fetched, and the hard playlist limit bounds
  the work.
- Reuse one `HttpClient`. Cancellation when: the active station's minutes
  change, the station changes, or the app disconnects. A station switch
  cancels only the outgoing queue; its downloaded files and snapshot stay.
- On start/refresh, a file that already exists in the cache counts as
  `Downloaded`; no re-download, including for stations that are not active.
- Prune only this service's cache. Prune a station when its playlist or its
  own minutes value changes. Keep only files whose episodes fall inside that
  station's budget; delete the rest. Never delete the file currently loaded
  by the player; leave it until the next refresh after it is unloaded.
- Downloads disabled for a station when `station.HostPaceEnabled ||
  station.IsAging` or its minutes value is 0; clear queued work and show the
  reason. Cached files for a host-paced/aging station are ignored for
  playback and removed by the prune, so a download cannot bypass server-side
  pacing.
- Expose `GetDownloadedPath(episodeId)` and a change notification so the UI
  can update badges while downloads run.

Error handling: a failed download marks that item `Failed`, keeps going with
the next item, and never leaves a `.part` file. Network errors are retried
once; other errors are not.

## App: playback from disk

`MainViewModel` asks the download service for a local path whenever the
current episode changes. If found, it loads the local file; otherwise it
loads the remote URL as today.

`SoundFlowAudioPlayer.Load` accepts an existing file path as well as a URL:
for a local file it opens a `FileStream` instead of the HTTP buffering path,
then uses the same `StreamDataProvider` setup. The combined
`StreamDataProvider`/device code is extracted into one private helper so the
two paths cannot drift.

## App: offline startup and continuation

New `Services/OfflineStateService.cs` persists a JSON file in
`%ApplicationData%/Ohrbob/offline-state.json` (atomic temp-file rename, no
token in it). It contains:

- schema version and `savedAt`;
- the active station id;
- the cached station list (station metadata, host-paced/aging flags, for the
  sidebar);
- a snapshot per station: its last fetched playlist, current episode
  metadata and `PositionSeconds`.

Flow:

- Every successful `InitializeAppAsync` / `RefreshStatusAsync` writes the
  active station's snapshot and updates the station list. The existing
  10-second sync timer also writes the playhead locally, so a kill or crash
  keeps the offline resume point.
- On startup the view model loads the snapshots first. If the network
  initialization then fails, the app stays `IsConnected`, sets an
  `IsOffline` flag, restores the active station and player state, and plays
  its current downloaded file. The sidebar and Up next panel come from the
  snapshots.
- Offline station selection is allowed for any station that has a snapshot
  and at least one downloaded file. It becomes the active station, restores
  that station's episode and position, and plays from disk. A snapshot whose
  current episode is missing falls back to the first downloaded playlist
  item. Stations without downloaded content stay in the sidebar but report
  "Not available offline" and are not started.
- Playback end while offline (or when the server cannot advance): if the
  next playlist item of the active station has a downloaded file, advance to
  it locally, reset the position, update the snapshot and play. Otherwise
  stop and show the normal empty state.
- When a refresh succeeds again, `IsOffline` clears, the app syncs the
  active station's current episode and position with `POST /stream/status`,
  and refreshes the playlist.
- The UI shows a small "Offline" chip while `IsOffline` is true and an
  "Offline" marker on stations with downloaded content. The settings dialog
  and Up next panel remain usable.

Retry: `RefreshAll` (F5 / pull-to-refresh) already re-runs initialization, so
it is the manual way back online. No new polling loop.

## App: settings dialog

Add an in-app overlay dialog, opened by a gear button in the sidebar footer
(next to "Disconnect"). Keep the existing "Listening as" profile link.

Content:
- Title names the active station, e.g. "Settings — Morning Show".
- "Buffer ahead" segmented/radio choices for **that station only**: Off, 10,
  15, 30, 60, 120 minutes.
- One sentence: whole upcoming episodes of this station are downloaded; more
  buffer uses more storage.
- If the active station is host-paced or aging, a warning banner inside the
  dialog: downloads stay off for this station and why.
- Close button. Changes apply immediately, persist, and affect only this
  station.

## App: Up next panel

- A right-side panel over the player content, opened by swiping right on the
  player surface (`SwipeGestureRecognizer`, horizontal, mouse enabled) and by
  a visible "Up next" button/handle for desktop and accessibility. Swipe left
  or the close button closes it; a scrim closes it too.
- Header "Up next" plus the station name.
- List: cover, title, artist/feed, published date, and a state badge:
  "Playing", "Downloaded", "Downloading…", "Queued", "Not downloaded",
  "Download failed".
- When downloads are disabled, the badges are replaced by the reason banner:
  - host-paced: "Downloads are off: episodes unlock when the host has
    listened to them."
  - aging: "Downloads are off: this station only rotates the newest episode
    of each feed."
  - buffer off: "Set a buffer in Settings to download episodes."
- Empty/loading/unavailable states: "No upcoming episodes" / "Loading…" /
  "Up next is not available on this server."
- Playlist refresh happens on init, station switch, skip, and settings change.

## Tests

Backend (`Ohrbob.Player.Tests/StreamControllerTests.cs`):
- playlist returns current then upcoming in ordered rotation;
- limit is applied and clamped;
- host-paced station only returns allowed episodes;
- empty station returns an empty list;
- `EndlessSpin` keeps cycling, non-endless wraps to the first unplayed
  episode and then stops;
- `GetNextEpisodeAsync` behavior is unchanged by the refactor.

App (`Ohrbob.App.Tests`):
- `ConfigService` round-trips per-station minutes, keeps stations
  independent, and reads a missing/unknown station as 0;
- `OhrbobClient` parses the playlist and treats 404 as unavailable;
- download service downloads in order, stops at that station's target,
  resumes from cache, marks failures, prunes only that station's files,
  cancels on station change but keeps the outgoing station's downloads;
- station A and station B keep independent buffer targets and caches;
- download disabled for host-paced and aging stations, with the reason
  exposed to the view model;
- current downloaded episode loads the local path;
- offline snapshots round-trip for multiple stations, and a failed init
  restores the station list, active station, playlist, current episode and
  position;
- offline station switching works for a downloaded station and is refused
  for a station without downloads;
- playback end offline advances to the next downloaded episode and stops
  when there is none;
- a successful refresh clears offline mode and syncs the current state;
- settings dialog changes persist and update the view model;
- Up next panel shows items and badges, and shows the disabled reason;
- existing player/layout tests still pass.

## Open questions

None.
