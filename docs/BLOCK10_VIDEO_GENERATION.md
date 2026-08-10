# Block 10 — Scene video generation, queue UI, and resumability

Block 10 extends the persistent generation model from keyframes to animated scene clips without introducing a second queue or provider-specific domain model.

## Flow

```text
approved Start/End keyframe variants
        ↓
VideoGenerationCoordinator
        ↓
capability-compatible provider/model plan
        ↓
persist SceneClipVariant (Planned)
        ↓
persist scene.video.generate job + keyframe dependencies
        ↓
background JobProcessor
        ↓
VideoGenerationJobExecutionDispatcher
        ↓
IImageToVideoProvider
        ↓
materialize generated media
        ↓
media_assets + completed clip variant
```

HTTP generation requests only enqueue durable work. The persisted job graph remains authoritative and background execution is handled by the existing Block 4 worker.

## Clip provenance and non-destructive regeneration

Every `SceneClipVariant` persists:

- project and scene IDs
- variant number
- immutable prompt-version ID
- approved Start keyframe variant ID
- optional approved End keyframe variant ID
- generation job ID
- generated media asset ID
- actual provider/model used
- generation state and selected flag
- duration, aspect ratio, and resolution
- estimated/actual cost and currency
- creation/update timestamps

Regeneration always appends a variant. Selecting a newer completed clip changes only the selection reference; older successful variants remain recoverable and a selected variant cannot be deleted accidentally.

Clip variants and per-scene video settings use versioned project-settings keys, while jobs/attempts/dependencies/provider task IDs remain in the existing persistent job repository.

## Approval boundary

Animation requires the **current** keyframe selection to be approved. The coordinator resolves the approved Start variant and, when configured, the approved End variant and their persisted media locations.

The video job stores dependencies on the associated keyframe generation jobs. This makes the dependency graph explicit and restart-safe without blocking the HTTP request.

## Capability-aware request construction

The coordinator routes only providers/models that:

- are enabled by provider settings
- allow `ImageToVideo`
- advertise `ImageToVideo`
- support a Start frame
- support the project aspect ratio when constrained
- resolve to a provider-supported duration
- resolve to a provider-supported resolution
- support an End frame when the scene requests one

Simple Mode uses automatic routing. Advanced/Custom can persist explicit provider/model, duration, resolution, and End-frame settings. Custom can also disable fallback.

## Fallback policy

When fallback is enabled, alternatives are persisted in the job payload only when they can preserve the **same** generation semantics as the primary request:

- Start-frame support
- End-frame support when requested
- resolved duration
- aspect ratio
- resolved resolution

Fallback is allowed for operational/provider failures such as quota/credits, rate limits, provider outage, authentication, unsupported adapter/capability, network failure, timeout, and transient failure.

Fallback is deliberately not used for moderation rejection, invalid request parameters, or permanent generation failure. When a fallback succeeds, the clip variant is updated with the provider/model that actually produced the media.

The current catalog has one offline mock video provider, so production fallback becomes useful as real adapters are added later without changing the job/domain contract.

## Generated media

The mock video provider returns a provider asset URI just like a remote adapter. The dispatcher materializes it into local generated media and writes `MediaAssetMetadata` with `MediaCreationSource.Generated`.

The clip preview endpoint serves the stored media through `IMediaStorage` and supports range processing for browser video playback.

## Generation Queue

`GenerationQueuePanel` uses:

1. `GET /api/jobs/` for initial persisted state.
2. `GET /api/jobs/events` as an SSE notification stream.
3. A persisted-state reload when the SSE connection signals `ready`.

The browser does not poll every scene for job state. EventSource reconnect behavior handles transient disconnections; the database remains authoritative when events are missed.

Queue rows expose state, elapsed time, attempts/retries, costs, next-run scheduling, errors, and actions. Provider/model detail is hidden in Simple Mode.

Existing Block 4 APIs provide job pause/resume/retry/restart/cancel plus project and scene scope actions.

## Error and restart behavior

Provider failures are normalized through `ProviderFailure` and then mapped by the persistent JobService into waiting/retry/rejected/permanent states. In particular, quota/credit failures enter the existing `WaitingForQuota` path.

Provider task IDs returned by adapters are carried in `JobExecutionResult` so the job engine can persist them and use its existing startup-reconciliation behavior rather than blindly duplicating known provider-side work.

## API

```text
GET    /api/projects/{projectId}/scenes/{sceneId}/clips/
GET    /api/projects/{projectId}/scenes/{sceneId}/clips/settings
PUT    /api/projects/{projectId}/scenes/{sceneId}/clips/settings
POST   /api/projects/{projectId}/scenes/{sceneId}/clips/generate
GET    /api/projects/{projectId}/scenes/{sceneId}/clips/{variantId}/preview
POST   /api/projects/{projectId}/scenes/{sceneId}/clips/{variantId}/select
DELETE /api/projects/{projectId}/scenes/{sceneId}/clips/{variantId}
```

The existing `/api/jobs/...` endpoints and `/api/jobs/events` SSE stream are shared by keyframe and video work.

## Validation status

Repository-side source/tests exist for:

- settings persistence
- mock video materialization and generated media metadata
- non-destructive clip selection/history
- provider fallback and Custom fallback-disable behavior
- mounted animation/queue UI and SSE usage

These are **not claimed as passed** until the commands/scenarios in `TESTPLAN.md` execute successfully. Real video-provider integration remains intentionally open in `PLAN.md` until the mock validation gate passes.
