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
  |      provider capability/settings ports
  |      persistent job coordination
  |
  +--> Infrastructure
         DuckDB repositories/settings/jobs
         filesystem media storage
         ffprobe / FFmpeg analysis + previews
         credential resolver
         mock Director/Image/Video providers
         capability-specific provider resolvers
         keyframe/video job dispatchers
         persistent background worker + SSE change hub
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

## Domain model

Important implemented domain areas now include:

- `MusicVideoProject` and stable Song/Character/Style/Location references
- media metadata
- explicit persistent generation job state machine
- immutable/versioned song analysis and lyric timing
- reusable visual/asset library models
- project-specific Character continuity/state
- normalized Director controls
- versioned Visual Arc and Storyboard
- structured scene creative details
- immutable prompt versions/templates
- `KeyframeVariant` and scene keyframe approval
- `SceneClipVariant`
- per-scene keyframe/video generation settings

### Planning provenance

A storyboard links one exact Song Analysis and Visual Arc. Scene identity remains stable across storyboard versions. Each scene's selected prompt is an immutable `PromptVersionId`.

Keyframes reference that prompt version directly. Animated clips then reference:

- the same prompt version
- approved Start keyframe variant
- optional approved End keyframe variant
- generation job
- actual provider/model
- generated media asset

This forms an auditable chain:

```text
Song asset
  → SongAnalysisId
  → VisualArcId
  → StoryboardVersionId / SceneId
  → PromptVersionId
  → KeyframeVariantId(s)
  → SceneClipVariantId
  → MediaAssetId
```

Regeneration appends new variants and never overwrites prior successful media.

## Application layer

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

`KeyframeGenerationCoordinator` resolves the exact selected prompt and a capability-compatible image provider/model. It builds continuity references from reusable Character/Style/Location state while respecting provider limits, persists a planned variant, and then enqueues a durable `keyframe.image.generate` job.

The variant exists before worker execution, closing the race where a fast worker could otherwise finish before generation provenance had been persisted.

### Video coordination

`VideoGenerationCoordinator` requires the **current keyframe selection to be approved**. It resolves approved Start/optional End media and creates a `scene.video.generate` job that depends on the corresponding keyframe jobs.

Provider/model selection requires `ImageToVideo` plus Start-frame support. The coordinator resolves provider-supported duration/resolution and validates project aspect ratio and optional End-frame capability before persistence.

HTTP does not wait for provider work. The persisted job/dependency graph is the orchestration source of truth.

### Compatible fallback plan

Fallback is a provider-independent policy, not a provider implementation detail.

When enabled, the coordinator persists only alternatives that can preserve the same:

- Start-frame contract
- End-frame contract when used
- resolved duration
- project aspect ratio
- resolved resolution

The video dispatcher can move to these alternatives for operational failures such as quota/credits, rate limiting, outage, authentication, unsupported adapter capability, network, timeout, or transient failure.

Moderation rejection, invalid parameters, and permanent failures do not silently change provider.

If a fallback succeeds, `SceneClipVariant.ProviderId/ModelId` are updated to the adapter that actually produced the media. Custom mode can disable fallback entirely.

## Persistent jobs

The existing Block 4 state machine remains shared by keyframes and clips:

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

Jobs persist:

- definition/payload
- project/scene/parent IDs
- provider/model
- dependencies
- attempts/retries
- scheduling
- provider task ID
- errors
- estimated/actual cost
- claim/lease metadata

Generation-specific dispatch is composed as a chain:

```text
VideoGenerationJobExecutionDispatcher
  └─ non-video → GenerationJobExecutionDispatcher
                     └─ non-keyframe → MockJobExecutionDispatcher
```

This lets new generation job types share one worker/state machine rather than create parallel queue systems.

Provider task IDs returned by an adapter are carried in `JobExecutionResult` and persisted by `JobService`. Startup recovery therefore reuses the existing known-provider-task reconciliation path instead of blindly resubmitting remote work.

## Infrastructure persistence

DuckDB remains authoritative for core structured metadata/jobs. Large media bytes remain filesystem data.

Current core tables include:

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

Versioned JSON stored through `IProjectSettingsRepository` currently covers:

- Visual Arc/storyboard/prompt histories
- keyframe variants
- keyframe approvals/settings
- clip variants
- video-generation settings

These are behind application repository interfaces, so moving them to dedicated tables later does not change domain/application consumers.

## Media storage

`LocalMediaPathResolver` centralizes root/path safety.

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

Generated keyframes are stored in `keyframes/`; animated clips are stored in `generated/`. Both receive `MediaAssetMetadata` with `MediaCreationSource.Generated`.

Preview endpoints reopen media via `IMediaStorage` and are range-enabled for normal browser image/video usage.

## Provider abstractions

Provider capability descriptors remain the decision boundary. Current mock providers cover Director, image generation/editing, video generation, image-to-video, and video-to-video without requiring paid credentials.

Credential settings persist references, never resolved plaintext secrets.

`ImageGenerationProviderResolver` and `ImageToVideoProviderResolver` are infrastructure mappings from provider IDs to concrete adapters. Application coordinators only depend on provider capability/contracts.

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
/api/providers/...
/api/jobs/...
/api/jobs/events
```

Keyframe/clip generation endpoints return after durable enqueueing; provider execution happens in the worker.

## Frontend architecture

Feature-oriented structure now includes:

```text
src/features/projects/
src/features/analysis/
src/features/library/
src/features/planning/
src/features/generation/
  KeyframeWorkspace.tsx
  VideoGenerationWorkspace.tsx
  GenerationQueuePanel.tsx
```

### Progressive disclosure

Simple Mode uses automatic capability routing and hides provider/model/seed/raw-provider details. Advanced/Custom expose only generation controls supported by the selected model. Custom additionally controls fallback policy.

### Generation Queue

`GenerationQueuePanel` performs an initial persisted `GET /api/jobs/` read, then subscribes to `/api/jobs/events` with browser `EventSource`.

SSE events are notifications, not state storage. On SSE `ready`/reconnect, the frontend reloads persisted jobs. There is no per-scene job polling loop for the global queue.

Queue actions call the same persisted job APIs used elsewhere: pause/resume/retry/restart/cancel plus project/scene scope operations.

## FFmpeg / ffprobe boundary

All current process execution uses typed `ProcessStartInfo.ArgumentList` rather than shell-assembled user input.

Implemented uses:

- ffprobe authoritative audio metadata
- FFmpeg streaming waveform/energy/rhythm analysis
- FFmpeg bounded image/video preview generation for the asset library

Block 11 will extend this deterministic boundary to clip assembly, preview rendering, and final output.

## Data-loss/security boundaries

- secrets are credential references, not plaintext project/DuckDB/export data
- resolved media paths cannot escape configured roots
- user file names are validated
- FFmpeg/ffprobe receive typed arguments
- successful generated keyframes/clips are non-destructive variants
- selected variants cannot be silently deleted
- referenced reusable library items/assets are protected
- planning edits create immutable versions rather than overwriting provenance
- animation cannot proceed until the current keyframe selection is explicitly approved
- fallback candidates cannot silently change resolved generation dimensions

## Tests and deferred execution

Repository-side tests now cover architecture/persistence/providers/jobs, project/song/analysis/library/planning invariants, keyframe generation, clip generation, non-destructive variants, and video-provider fallback policy. Frontend source tests cover mounted keyframe/video/queue workflows and SSE usage.

These tests are **not considered passed until executed**. `TESTPLAN.md` is the authoritative validation matrix for build/lint/typecheck/unit/integration/browser/restart/fault-injection proof.

See `docs/BLOCK9_KEYFRAME_GENERATION.md` and `docs/BLOCK10_VIDEO_GENERATION.md` for focused generation-flow documentation.
