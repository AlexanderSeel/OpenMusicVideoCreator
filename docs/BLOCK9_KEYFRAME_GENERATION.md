# Block 9 — Keyframe generation implementation

This note documents the repository-side Block 9 mock/offline implementation. Executable proof remains in `TESTPLAN.md`; nothing in this document implies that build, tests, typecheck, browser behavior, or provider calls have executed successfully.

## Implemented flow

```text
Storyboard scene + selected PromptVersionId
  -> KeyframeGenerationCoordinator
  -> capability-aware image provider/model selection
  -> persisted Planned KeyframeVariant
  -> persisted generation Job
  -> attach JobId and mark variant Queued
  -> existing persistent job worker
  -> GenerationJobExecutionDispatcher
  -> IImageGenerationProvider
  -> generated asset materialized locally
  -> IMediaStorage + MediaAssetMetadata
  -> completed non-destructive KeyframeVariant
  -> user select / compare / delete / approve
```

The current resolver exposes `mock-image` only. The first real image adapter is intentionally gated on successful local validation of this path and must remain credential-optional.

## Provenance and non-destructive behavior

Each keyframe variant persists:

- project and scene IDs
- Start or End role
- monotonic scene/role variant number
- immutable `PromptVersionId`
- persisted `JobId`
- generated `MediaAssetId` after completion
- provider/model attribution
- state
- selected flag
- estimated/actual cost and currency
- timestamps

A new generation attempt creates a new variant. Successful older variants are not overwritten. Selection is a reference; the selected completed variant cannot be deleted until another completed variant is selected.

The variant is first stored as `Planned`, then the persisted job is enqueued, then its `JobId` is attached and the variant becomes `Queued`. This closes the worker-wakeup race where a job could otherwise execute before durable prompt/variant provenance existed.

## Provider routing

`KeyframeGenerationCoordinator` resolves only providers that are:

- enabled
- allowed to perform `ImageGeneration`
- backed by a registered image-generation model with that capability

Automatic routing uses configured provider priority/default image models. Advanced/Custom scene settings may pin provider/model and supported controls. Simple Mode remains automatic and does not expose provider IDs, model IDs, seed, negative prompt, or raw provider settings.

Optional settings are validated against model capabilities. Seed and negative prompt are rejected when the selected model does not support them. Resolution is resolved from the project and model-supported values.

## Visual references

When the selected model supports references, the coordinator assembles them in continuity-oriented order:

1. selected project-specific Character outfit assets
2. Character base/reference assets
3. Style assets
4. Location assets

References are deduplicated and capped at the model's `MaxReferences`. The generated job payload stores media locations, not credential secrets.

## Persistent execution

Keyframe generation uses the existing job engine with job type:

```text
keyframe.image.generate
```

`GenerationJobExecutionDispatcher` delegates unrelated jobs to the existing mock dispatcher and handles keyframe image jobs through `IImageGenerationProvider`.

Successful provider results are materialized before a variant becomes completed:

- mock URIs become a deterministic local SVG preview asset
- data URIs are decoded
- HTTP(S) results are downloaded
- bytes are saved through `IMediaStorage` in the project keyframe area
- `MediaAssetMetadata` records checksum, MIME, dimensions, size, generated source, and project ownership

The completed variant points to that durable local media asset rather than a transient provider URL.

Retryable/provider-wait/quota-style failures keep the variant pending while the existing job engine owns retry/wait behavior. Local validation must specifically confirm that retry exhaustion/cancellation eventually produces a terminal variant state rather than leaving a permanently pending card; see `TESTPLAN.md`.

## HTTP surface

Routes are under:

```text
/api/projects/{projectId}/scenes/{sceneId}/keyframes
```

Implemented endpoints:

```text
GET    /
GET    /settings
PUT    /settings
POST   /generate
GET    /{variantId}/preview
POST   /{variantId}/select
DELETE /{variantId}
GET    /approval
POST   /approval
DELETE /approval
```

Generation returns HTTP 202 after variants/jobs are persisted; it does not synchronously wait for image generation.

## Frontend

`KeyframeWorkspace` is mounted after the Director workspace in the project flow. It provides:

- scene selection
- Start and optional End keyframe generation
- non-destructive regeneration
- active-generation refresh
- stored preview display
- variant comparison/select/delete
- estimated/actual cost display
- approval/revoke before later animation
- Advanced/Custom provider/model/resolution and capability-supported settings
- automatic hidden routing in Simple Mode

The dedicated `frontend/src/api/keyframes.ts` client is typed against the implemented API surface. The committed OpenAPI TypeScript snapshot still requires local regeneration after the updated backend is running; that is explicitly tracked in `TESTPLAN.md`.

## Repository-side tests added

`KeyframeGenerationFlowTests` covers source-level test scenarios for:

- scene generation settings surviving repository recreation
- Planned -> persisted Job attachment without changing `PromptVersionId`
- mock image job -> durable generated media -> completed new variant while an older selected variant remains intact

`keyframe-workspace-ui.test.mjs` covers the mounted Director -> Keyframe workflow, required client actions, progressive disclosure, variants, approval, and active-generation refresh structure.

These tests have not been claimed as passed until they actually execute.

## Remaining Block 9 item

The mock/offline repository implementation is complete. The remaining PLAN item is the first real image provider integration. It must be added only after the Block 9 mock validation matrix executes successfully, remain disabled without credentials, and be contract-tested without requiring paid calls in normal automated tests.
