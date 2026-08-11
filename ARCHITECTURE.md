# OpenMusicVideoCreator Architecture

This document describes architecture implemented in the repository today. `PLAN.md` tracks repository-side implementation; `TESTPLAN.md` tracks validation that still needs to execute locally.

## Deployment shape

The MVP remains a **modular monolith / service-oriented application**.

```text
Browser
  |
  | HTTP/JSON + SSE
  v
Next.js / React
  |
  v
ASP.NET Core API host
  |
  +--> Application
  |      projects + media
  |      song analysis + lyric timing
  |      reusable visual libraries
  |      Director / Visual Arc / storyboard / prompts
  |      keyframe + clip generation coordination
  |      versioned Advanced timeline editing
  |      deterministic project rendering
  |      provider capability/settings ports
  |      persistent job coordination
  |
  +--> Infrastructure
         DuckDB repositories/settings/jobs
         filesystem media storage
         ffprobe / FFmpeg analysis + previews + rendering
         credential resolver
         mock Director/Image/Video providers
         capability-specific provider resolvers
         keyframe/video/render job dispatchers
         persistent background worker + SSE change hub
         active-local-execution cancellation signals
```

Logical boundaries can be split later only if a concrete scaling/deployment requirement justifies it.

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

- **Domain** owns durable concepts and invariants only.
- **Application** owns use cases and ports/interfaces.
- **Infrastructure** owns DuckDB, filesystem/process/provider implementations and external credentials.
- **API** maps HTTP contracts to application operations.
- **Frontend** never becomes an alternative source of truth for persisted state.

## Domain model and provenance

Important durable concepts include:

- `MusicVideoProject` and stable Song/Character/Style/Location references
- media metadata
- explicit persistent generation-job state machine
- immutable/versioned Song Analysis and lyric timing
- reusable visual/asset library models
- project-specific Character continuity/state
- normalized Director controls
- versioned Visual Arc and Storyboard
- structured scene creative details
- immutable prompt versions/templates
- `KeyframeVariant` and scene keyframe approval
- `SceneClipVariant`
- `ProjectTimelineVersion` including clip/overlay/effect/subtitle edit state
- `ProjectRenderManifest`, `ProjectRenderRecord`, and render attempts

The primary provenance chain is:

```text
Original Song media
  → SongAnalysisId
  → VisualArcId
  → StoryboardVersionId / SceneId
  → PromptVersionId
  → KeyframeVariantId(s)
  → SceneClipVariantId / generated clip media
  → ProjectTimelineVersionId (optional Advanced edit layer)
  → ProjectRenderManifest / timeline SHA-256
  → rendered MediaAssetId
```

Regeneration and editing append versions/variants rather than overwriting successful assets or prior decisions.

## Planning and generation application layer

Important ports/use cases include:

- project/settings/media repositories
- `IMediaStorage`
- song-analysis and lyric-timing repositories/adapters
- visual/asset library repositories and media preview ports
- Visual Arc / Storyboard / Prompt history repositories
- Director planning provider/service
- provider catalog/settings/credential abstractions
- image/image-edit/video/image-to-video/video-to-video capability interfaces
- keyframe variant/settings/approval services
- `KeyframeGenerationCoordinator`
- clip variant/video settings services
- `VideoGenerationCoordinator`
- persistent job repository/queue/change stream/dispatcher

### Keyframe coordination

`KeyframeGenerationCoordinator` resolves the selected immutable prompt plus a capability-compatible image provider/model. It builds continuity references, persists a planned variant first, then enqueues durable generation work. This closes the worker race where generation might otherwise finish before provenance exists.

### Scene-video coordination

`VideoGenerationCoordinator` requires current keyframe approval. It resolves approved Start/optional End media and creates a `scene.video.generate` job with dependencies on the corresponding keyframe jobs.

Provider/model selection requires Image-to-Video capability plus supported Start/End-frame, duration, aspect-ratio, and resolution semantics.

Fallback candidates are persisted only when those semantics can be preserved. Operational failures may move to compatible fallbacks; moderation rejection, invalid parameters, and permanent failures do not silently change provider.

## Persistent jobs and active cancellation

Keyframe, clip, and render work share the same persisted job graph/state machine:

```text
Draft
Queued
Submitting
ProviderQueued
Generating
Downloading
Validating
Completed
Paused
WaitingForQuota
WaitingForProvider
WaitingForDependency
RetryScheduled
Rejected
FailedRetryable
FailedPermanent
Cancelled
```

Jobs persist definition/payload, project/scene/parent IDs, provider/model, dependencies, attempts/retries, scheduling, provider task IDs, errors, cost, and claim/lease metadata.

The execution dispatcher chain is:

```text
ProjectRenderJobExecutionDispatcher
  └─ non-render → VideoGenerationJobExecutionDispatcher
                    └─ non-video → GenerationJobExecutionDispatcher
                                      └─ non-keyframe → MockJobExecutionDispatcher
```

Persisted state remains authoritative. `IJobExecutionCancellationRegistry` is intentionally **ephemeral**: it only propagates a persisted user cancellation into already-running local work such as FFmpeg. `JobProcessor` checks persisted `Cancelled` state before applying a dispatcher result so stale local success cannot overwrite cancellation.

Project/scene/single-job cancel APIs signal matching active execution tokens after persisting cancellation.

## Versioned Advanced timeline

`ProjectTimelineVersion` is the reversible editing layer between selected generated clips and deterministic rendering.

It pins:

- exact project, Storyboard, and original Song IDs
- parent timeline version
- `MusicTrackLocked = true`
- ordered `TimelineClip` records
- overlay lane records
- effect lane records
- timed subtitle records

A timeline clip keeps scene/variant/media provenance while storing edit decisions:

- timeline slot
- source in/source duration
- slight playback-rate change
- freeze extension
- transition kind/duration
- scale/position
- crop
- opacity
- brightness/contrast/saturation

Application operations create new versions for update, reorder, split, completed-variant replacement, overlay/effect/subtitle changes, reset, and restore. Restoring a compatible old version creates another new version.

`TimelineEditorService` refuses cross-project media and resolves the current Storyboard/Song before every edit. If a storyboard changes, current compatible timeline state is initialized first; stale clip IDs therefore fail instead of modifying stale state. Restoring a version tied to another Song or an older storyboard is rejected.

The frontend reuses persisted Block 6 analysis for music-reference lanes: waveform, quiet ranges, structure sections, phrase boundaries, beat/bar markers, and lyric timing.

The Scene Inspector is separated into Story, Character, Environment, Camera, Generation, and Prompt. Generation-specific provider fields remain in capability-aware generation workspaces; the timeline deals in provider-independent edit state plus completed variant references.

Editable composition controls cover:

- project-owned image/video overlays with timing, scale, position, and opacity
- bounded Fade-to-black, Grayscale, and Vignette effects
- timed subtitles with text, vertical position, size, and opacity

Every composition change creates a new timeline version.

## Deterministic project rendering

`ProjectRenderService` creates versioned Preview/Final render records. It uses the latest Advanced timeline only when its exact Storyboard and Song IDs match the current project state; stale timeline state is not silently applied.

`ProjectRenderManifest` pins:

- Storyboard and original Song
- optional `TimelineVersionId`
- selected clip variant/media provenance
- timing/source trim/playback/freeze
- transition metadata
- transforms/crop/color/opacity
- overlay/effect/subtitle lanes
- output dimensions/profile
- deterministic timeline SHA-256

All content-affecting values participate in the timeline hash, including subtitle text/timing/style. Preview and Final created from unchanged decisions therefore share the same timeline hash even though encoding settings differ.

### FFmpeg render boundary

`FfmpegProjectRenderEngine` resolves every media path through `LocalMediaPathResolver` and passes every FFmpeg argument through `ProcessStartInfo.ArgumentList`; no user path is assembled into a shell command.

Current filters apply:

- source trim
- playback-rate change
- freeze/short-source padding
- crop/scale/position
- brightness/contrast/saturation
- opacity
- Fade
- true neighboring Crossfade using FFmpeg `xfade`
- overlay lanes
- Fade-to-black, Grayscale, and Vignette effects
- burned-in timed subtitles using escaped `drawtext`

For Crossfade, the outgoing clip is extended by exactly the incoming transition duration and `xfade` starts at the incoming clip's nominal timeline boundary. This preserves the original song/timeline duration.

Subtitle filter values use `expansion=none` and parser escaping for text characters; subtitle text is never shell input.

Scene clips and overlays may contain audio, but their audio is never mapped. The protected original Song input is the sole output audio source.

After FFmpeg writes output, the existing `IMediaProbe`/ffprobe adapter validates duration and valid audio presence before a completed render/media record is published.

On cancellation, invalid output, or persistence failure, the dispatcher best-effort removes newly created render bytes/media metadata only; source/generated inputs remain untouched.

Render attempts persist start/completion, outcome, error, and deterministic command log. Automatic transient retries close the failed attempt and keep the render pending; retry exhaustion becomes terminal. Manual retry restarts the same persisted job/manifest.

## Infrastructure persistence

DuckDB remains authoritative for core structured metadata/jobs. Large media bytes remain filesystem data.

Core tables include:

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

Versioned JSON behind repository interfaces currently covers:

- Visual Arc/storyboard/prompt histories
- keyframe variants/approvals/settings
- clip variants/video-generation settings
- render history (`render.history.v1`)
- Advanced timeline versions (`timeline.versions.v1`)

These repository interfaces allow later migration to dedicated tables without changing Domain/Application consumers.

## Media storage

`LocalMediaPathResolver` centralizes root/path-traversal safety.

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

- original Song/reference media remains read-only from generation/timeline/render flows
- generated keyframes/clips receive `MediaCreationSource.Generated`
- final/proxy renders receive `MediaCreationSource.Rendered`
- browser media routes reopen media through metadata and support range processing where appropriate

## Provider abstractions and credentials

Provider capability descriptors remain the generation decision boundary. Current mock providers cover Director, image generation/editing, video generation, image-to-video, and video-to-video without paid credentials.

Credential settings persist references, never resolved plaintext secrets.

`ImageGenerationProviderResolver` and `ImageToVideoProviderResolver` are Infrastructure mappings from provider IDs to concrete adapters; Application coordinators depend only on contracts/capabilities.

Real image/video provider adapters remain open PLAN items until mock validation is proven.

## API surface

Implemented groups include:

```text
/api/projects/...
/api/projects/{id}/song
/api/projects/{projectId}/analysis/...
/api/library/...
/api/projects/{projectId}/director/...
/api/projects/{projectId}/scenes/{sceneId}/keyframes/...
/api/projects/{projectId}/scenes/{sceneId}/clips/...
/api/projects/{projectId}/timeline/...
/api/projects/{projectId}/renders/...
/api/providers/...
/api/jobs/...
/api/jobs/events
```

Generation/render POST endpoints persist/enqueue work and return without waiting for remote/FFmpeg execution.

## Frontend architecture

Feature-oriented structure includes:

```text
src/features/projects/
src/features/analysis/
src/features/library/
src/features/planning/
src/features/generation/
  KeyframeWorkspace.tsx
  VideoGenerationWorkspace.tsx
  GenerationQueuePanel.tsx
src/features/timeline/
  AdvancedTimelineAnalysisPanel.tsx
  TimelineAnalysisLanes.tsx
  AdvancedTimelineEditor.tsx
  TimelineCompositionControls.tsx
src/features/rendering/
  ProjectRenderWorkspace.tsx
```

### Progressive disclosure

Simple Mode hides provider internals and the Advanced timeline. Advanced/Custom show capability-supported generation settings plus timeline versions/analysis lanes/Scene Inspector/composition controls. Custom additionally controls generation fallback policy.

### Generation Queue

`GenerationQueuePanel` initially reads persisted `/api/jobs/`, then subscribes to `/api/jobs/events` with `EventSource`. SSE is notification-only; reconnect reloads persisted state. There is no per-scene polling loop for the global queue.

### Render workspace

`ProjectRenderWorkspace` exposes Preview/Final queueing, versioned history, provenance, attempts, cancel/retry, deterministic command disclosure, and output download.

## Security/data-loss boundaries

- secrets are credential references, not plaintext project/DuckDB/export data
- resolved media paths cannot escape configured roots
- user file names are validated
- FFmpeg/ffprobe receive typed argument lists
- successful generated keyframes/clips are non-destructive variants
- selected variants cannot be silently deleted
- reusable referenced library items/assets are protected
- planning/timeline/render decisions are versioned instead of destructively overwritten
- animation requires explicit current keyframe approval
- fallback candidates cannot silently change resolved generation semantics
- Advanced timeline overlays must reference current-project media
- subtitle text is validated/escaped for FFmpeg filters and is never executed as shell input
- stale Storyboard/Song timeline versions cannot be silently edited/restored into current project state
- render cancellation cannot publish stale local success over persisted `Cancelled` state

## Tests and deferred execution

Repository-side tests cover architecture/persistence/providers/jobs, project/song/analysis/library/planning invariants, keyframe/clip generation, fallback policy, render provenance/lifecycle/cancellation, versioned Advanced timeline editing, current-storyboard guards, true Crossfade construction, subtitle versioning/escaping, and Advanced FFmpeg argument construction. Frontend source tests cover generation/queue/render/Advanced timeline/composition structure.

These tests are **not considered passed until executed**. `TESTPLAN.md` remains authoritative for build/lint/typecheck/unit/integration/browser/restart/FFmpeg/fault-injection proof.

Focused documentation:

- `docs/BLOCK9_KEYFRAME_GENERATION.md`
- `docs/BLOCK10_VIDEO_GENERATION.md`
- `docs/BLOCK11_RENDERING.md`
- `docs/BLOCK12_ADVANCED_TIMELINE.md`
