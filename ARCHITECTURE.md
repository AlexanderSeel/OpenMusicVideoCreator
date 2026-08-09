# OpenMusicVideoCreator Architecture

This document describes architecture implemented in the repository today. `PLAN.md` tracks implementation progress; `TESTPLAN.md` tracks validation still to execute locally.

## Deployment shape

The MVP remains a **modular monolith / service-oriented application**, not a distributed microservice system.

```text
Browser
  |
  v
Next.js / React frontend
  |
  | typed HTTP/JSON + SSE
  v
ASP.NET Core API host
  |
  +--> Application use cases/contracts
  |      projects + media
  |      song analysis + lyric timing
  |      reusable visual/asset libraries
  |      project character continuity/state
  |      provider configuration/capabilities
  |      persistent job coordination
  |
  +--> Infrastructure adapters
         DuckDB repositories/migrations
         local project + global library media storage
         ffprobe / FFmpeg signal + preview adapters
         credential resolver
         mock AI providers
         persistent job worker/change hub
```

Logical boundaries can be split later only if a concrete scaling/deployment need justifies it.

## Dependency direction

```text
Domain
  ^
  |
Application
  ^
  |
Infrastructure

Application <--- API
Infrastructure <--- API
```

- Domain owns durable concepts/invariants only.
- Application owns use cases and ports/interfaces.
- Infrastructure owns DuckDB, filesystem, process execution, provider implementations, and external credentials.
- API maps HTTP contracts to Application operations.
- Frontend never becomes an alternative source of truth for persisted state.

## Domain

Implemented domain areas include:

- `MusicVideoProject` / `ProjectDraft`
- stable project references: Song, Character, Style, Location, AdditionalMedia
- media metadata
- explicit persistent generation-job state machine
- versioned `SongAnalysis`
- waveform/energy/beats/sections plus derived bars/phrases/quiet ranges
- deliberately bounded/low-confidence `VocalActivityEstimate`
- versioned `LyricTimingAnalysis`
- `VisualLibraryItem` with Character/Style/Location payloads
- `AssetLibraryEntry`
- `ProjectCharacterState`

### Reusable visual library model

A project stores only stable Character/Style/Location IDs. Reusable metadata remains in the global Library.

Character data includes:

- reference type
- appearance description
- forbidden changes
- outfits and outfit asset IDs
- default continuity locks

Style data includes prompt, camera, lighting, and animation characteristics.

Location data includes environment, constraints, lighting, weather, and time of day.

Project-specific character state is intentionally separate from the Character Library item. It stores selected outfit, continuity lock overrides, and normalized state values such as presence/confidence/isolation. This is the seed model for later timeline curves without making global Character metadata project-specific.

## Application

Important ports/use cases now include:

- project/settings/media repositories
- `IMediaStorage`
- `IMediaProbe` / `IAudioSignalAnalyzer` / `ISongAnalysisRepository`
- `ILyricTimingRepository`
- `IVisualLibraryRepository`
- `IAssetLibraryRepository`
- `IProjectCharacterStateRepository`
- `ILibraryMediaStorage`
- `IMediaPreviewGenerator`
- provider capability interfaces/catalog/credentials
- persistent job repository/queue/change stream/dispatcher
- render-engine boundary

### Song analysis

`SongAnalysisService` loads the authoritative Song reference, probes media, analyzes the signal, creates a new immutable analysis version, and persists editable Structure Map sections.

Rhythm derivatives (bars/phrases/quiet ranges) are calculated from persisted base signal data instead of stored redundantly.

Vocal/instrumental activity is a heuristic energy + zero-crossing estimate with intentionally bounded low confidence. Low-information input may return no estimate rather than fabricated certainty.

### Lyric timing

`LyricTimingService` consumes provider-neutral timestamped transcription segments. It aligns them sequentially to the exact supplied lyric lines and persists timing/confidence separately from project lyrics.

Each timing version records:

- source media asset ID
- exact SongAnalysis ID
- SHA-256 of supplied lyrics
- exact supplied line text
- optional start/end suggestion and confidence

Transcription never silently rewrites authoritative lyrics.

### Visual Library

`VisualLibraryService` owns create/update/search/filter/delete behavior and validates all referenced Asset Library IDs. Deletion is blocked while any project still references the Character/Style/Location.

`AssetLibraryService` owns visual upload validation, source/preview media metadata, search/tags/favorites/source tracking, and reference-aware deletion. Removing an asset index entry intentionally does not silently delete underlying media bytes.

`ProjectCharacterStateService` validates that the project actually references the Character, selected outfits belong to that Character, and state values are normalized to 0–1 before persistence.

## Infrastructure

### DuckDB

DuckDB is authoritative for structured metadata. Current schema version is **5**.

```text
schema_migrations
projects
project_targets
project_references
application_settings
project_settings
media_assets
jobs
job_dependencies
job_attempts
song_analyses
lyric_timing_analyses
library_assets
visual_library_items
project_character_states
```

Schema evolution:

- v1 — project/settings/media foundation
- v2 — persistent jobs/dependencies/attempts
- v3 — versioned song analyses
- v4 — vocal estimate + lyric timing versions
- v5 — global visual/asset libraries + project Character state

Searchable library fields (kind/name/favorite) are first-class columns. Tags, typed detail payloads, asset-ID lists, continuity locks, and state maps are version-tolerant JSON columns.

Large audio/image/video bytes never live in DuckDB.

### Media storage

`LocalMediaPathResolver` is the single root/path-traversal policy used by project storage, global library storage, ffprobe, and FFmpeg adapters.

```text
projectsRoot/
  library/
    originals/
    previews/
  {project-id}/
    source/
    references/
    analysis/
    keyframes/
    generated/
    proxies/
    renders/
```

Global visual library media has `project_id = NULL` in `media_assets`; project media keeps its project association.

### FFmpeg / ffprobe

All process execution uses `ProcessStartInfo.ArgumentList`; no shell command string is assembled from user input.

Implemented uses:

- ffprobe: authoritative audio metadata
- FFmpeg: streaming PCM for waveform/energy/rhythm analysis
- FFmpeg: first-frame/visual PNG preview generation with bounded output size

The preview adapter resolves the source through the same safe media root and writes its result through `ILibraryMediaStorage`.

## API

Implemented product APIs now include:

```text
/api/projects/...
/api/projects/{id}/song
/api/projects/{projectId}/analysis/...
/api/projects/{projectId}/analysis/lyrics/timing...
/api/library/items...
/api/library/assets...
/api/projects/{projectId}/characters/states...
/api/providers/...
/api/jobs/...
```

Library deletion conflicts return referencing project/library IDs so the UI can explain why deletion is blocked instead of silently detaching references.

Public enums serialize as readable strings. The committed frontend OpenAPI snapshot remains the TypeScript contract source.

## Frontend

Feature-oriented structure:

```text
src/features/projects/
  ProjectStudio.tsx
  ProjectSidebar.tsx
  ProjectForm.tsx
  projectModel.ts

src/features/analysis/
  SongAnalysisPanel.tsx

src/features/library/
  VisualLibraryPanel.tsx
  VisualReferenceSelector.tsx
  ProjectCharacterContinuity.tsx
```

### Project references

`VisualReferenceSelector` edits only `{ kind, referenceId }` project references. It does not copy appearance/style/location payloads into project state and preserves unrelated references such as Song.

### Library workspace

`VisualLibraryPanel` provides:

- Character/Style/Location create/edit/delete
- search/type filtering
- favorites
- global asset upload
- source metadata
- generated previews
- reference-aware conflict messages

### Character continuity

`ProjectCharacterContinuity` edits project-specific outfit, continuity locks, and normalized initial state values separately from the reusable global Character definition.

Simple Mode still hides provider IDs, model IDs, seeds, and raw provider JSON.

## Persistent jobs

Generation job state remains persisted in DuckDB. Normal resume does not regenerate completed work. Known provider task IDs survive restart and move into reconciliation instead of blind re-submission. SSE broadcasts are notifications only; persisted jobs are authoritative.

## Security / data-loss boundaries

- credentials are references, never plaintext project/DuckDB/export data
- project and global library filenames are validated as safe leaf names
- resolved media paths cannot escape the configured root
- FFmpeg/ffprobe receive typed argument lists rather than shell strings
- upload size/MIME/extension validation happens before accepted media becomes a library/project reference
- project/library metadata deletion does not silently destroy underlying user media
- successful/generated variants remain non-destructive
- referenced Character/Style/Location/Asset entries cannot be silently deleted

## Tests and deferred execution

Repository-side test code covers architecture, persistence, providers, jobs, project/song behavior, analysis/versioning/rhythm/lyrics, and visual-library invariants including cross-project reuse, deletion conflicts, durable character state, and path traversal.

Source-presence tests also protect the typed frontend contract and key Simple/Analysis/Library UI invariants.

These tests are **not considered passed until executed**. `TESTPLAN.md` contains the local Codex validation matrix and is the sole tracker for still-unexecuted build/lint/typecheck/test/FFmpeg/browser/manual proof.
