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
  |      AI Director + Visual Arc + storyboard + prompt history
  |      provider configuration/capabilities
  |      persistent job coordination
  |
  +--> Infrastructure adapters
         DuckDB repositories/migrations
         project-settings planning history
         local project + global library media storage
         ffprobe / FFmpeg signal + preview adapters
         credential resolver
         mock AI providers / structured mock Director
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
- normalized `DirectorControls`
- versioned `VisualArcVersion` / `VisualArcPoint`
- versioned `StoryboardVersion` / `StoryboardScene`
- structured `StoryboardSceneDetails`
- immutable `PromptVersion` plus versioned `PromptTemplate`

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

### Director planning model

A Visual Arc is immutable/versioned and linked to one exact `SongAnalysisId`. Its normalized controls remain part of the version so later scene/prompt edits can reuse the creative settings that actually produced the storyboard.

A storyboard is also immutable/versioned and links both the exact song analysis and exact Visual Arc. Scene identity remains stable across storyboard versions so prompt history and downstream generation variants can continue to reference the same logical scene.

`StoryboardSceneDetails` carries structured creative planning that should not be collapsed into one opaque prompt string:

- song section and associated lyric
- scene purpose
- emotion
- composition
- lighting
- environment motion
- visual symbolism
- continuity requirements

Core action/environment/camera/transition fields and Character/Style/Location IDs remain first-class scene data. This allows editing/reordering without reparsing generated text.

Prompt history stores Director Intent separately from the expanded provider prompt. Prompt template name/version and storyboard-version provenance are persisted with every revision. Downstream generation models use immutable `PromptVersionId` references rather than copying an unauditable prompt string.

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
- `IVisualArcRepository` / `IStoryboardRepository` / `IPromptHistoryRepository`
- `IDirectorPlanningProvider` / `DirectorPlanningService`
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

### AI Director / storyboard

`DirectorPlanningService` builds a provider-independent planning context from:

- the exact song-analysis version
- BPM/sections/phrases
- authoritative lyrics
- storyline/meaning/visual direction/mood/genre
- normalized Director controls
- attached Characters/Styles/Locations
- project-specific Character continuity state

A new plan intentionally uses the latest song analysis. Later Visual Arc edits, scene edits, reordering, and prompt regeneration **do not** silently adopt a newer analysis: the service resolves the storyboard's stored `SongAnalysisId` and `VisualArcId`, validates that those provenance links still match, and uses the referenced Visual Arc controls for prompt expansion.

Scene edit saves create a new storyboard version and a new prompt version only for the edited scene. Scene reorder keeps the existing ordered timing slots and moves scene content into those slots so timing stays contiguous/non-overlapping. Prompt-only regeneration creates a new prompt/storyboard version but never dispatches an image/video generation job.

Structured Director output is validated before persistence: Visual Arc points must be valid and ordered; scenes must contain structured creative details; scene timing must cover the complete song without gaps/overlaps; and referenced Character/Style/Location IDs must already be attached to the project.

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

Block 8 planning history does not need an additional table migration. `DuckDbPlanningRepository` persists versioned Visual Arc, storyboard, and prompt-history JSON through `IProjectSettingsRepository` under separate versioned keys. This retains durable restart behavior while keeping the planning repository behind application ports.

Large audio/image/video bytes never live in DuckDB.

### Structured mock Director

`StructuredMockDirectorProvider` is the offline Block 8 planning implementation. It derives a target scene count from song duration, prefers nearby section/phrase anchors with a pacing-relative snap tolerance, and enforces a hard minimum interval to avoid micro-scenes.

It emits structured scene data rather than only a final prose prompt. The application layer validates that output and performs prompt expansion using the current versioned template.

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
/api/projects/{projectId}/director/...
/api/providers/...
/api/jobs/...
```

Director routes expose planning, Visual Arc/current+history, storyboard/current+history, scene editing/reordering, and prompt history/regeneration.

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

src/features/planning/
  DirectorStoryboardPanel.tsx
  SceneReferenceEditor.tsx
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

### Director workspace

`DirectorStoryboardPanel` exposes all normalized Director controls, an editable Visual Arc, storyboard cards, and a detailed selected-scene inspector. The inspector edits structured creative scene fields plus Character/Style/Location references through `SceneReferenceEditor`.

Prompt history visibly separates Director Intent from Final Provider Prompt and identifies template versions. The prompt-only action calls only the planning endpoint; job/image/video generation stays a later explicit workflow.

Simple Mode still hides provider IDs, model IDs, seeds, and raw provider JSON.

## Persistent jobs

Generation job state remains persisted in DuckDB. Normal resume does not regenerate completed work. Known provider task IDs survive restart and move into reconciliation instead of blind re-submission. SSE broadcasts are notifications only; persisted jobs are authoritative.

Block 9 groundwork already models keyframe variants with immutable `PromptVersionId` provenance. It remains unfinished as a PLAN block until the full capability-routing/generation/UI/approval flow is implemented.

## Security / data-loss boundaries

- credentials are references, never plaintext project/DuckDB/export data
- project and global library filenames are validated as safe leaf names
- resolved media paths cannot escape the configured root
- FFmpeg/ffprobe receive typed argument lists rather than shell strings
- upload size/MIME/extension validation happens before accepted media becomes a library/project reference
- project/library metadata deletion does not silently destroy underlying user media
- successful/generated variants remain non-destructive
- referenced Character/Style/Location/Asset entries cannot be silently deleted
- Director edits create new versions rather than overwriting prior Visual Arc/storyboard/prompt history
- scene/prompt edits preserve exact song-analysis and Visual-Arc provenance

## Tests and deferred execution

Repository-side test code covers architecture, persistence, providers, jobs, project/song behavior, analysis/versioning/rhythm/lyrics, visual-library invariants, and Director planning invariants including music-aware scene pacing, structured scene details, planning-history persistence, and scene timing validation.

Source-presence tests protect the typed frontend contract and key Simple/Analysis/Library/Director UI invariants, including the planning client operations and actual wiring of the scene reference editor.

These tests are **not considered passed until executed**. `TESTPLAN.md` contains the local Codex validation matrix and is the sole tracker for still-unexecuted build/lint/typecheck/test/FFmpeg/browser/manual proof.
