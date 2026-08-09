# OpenMusicVideoCreator Architecture

This document describes the architecture that exists in the repository today. Future capabilities belong in `PLAN.md` until implemented.

## Deployment shape

The MVP is a **modular monolith / service-oriented application**, not a distributed microservice system.

```text
Browser
  |
  v
Next.js frontend
  |
  | HTTP / JSON + SSE, typed from OpenAPI
  v
ASP.NET Core API host
  |
  +--> Application
  |      +--> ProjectService
  |      +--> ProviderSettingsService
  |      +--> JobService / JobProcessor
  |      +--> repository/storage/provider/job contracts
  |      +--> render-engine boundary contract
  |
  +--> Infrastructure
         +--> DuckDB repositories
         +--> local filesystem media storage
         +--> provider catalog + mock adapters
         +--> credential resolver
         +--> job change hub + mock dispatcher
```

The backend can later split deployment units if a demonstrated scaling or operational requirement justifies it. Logical service boundaries must not be interpreted as a requirement to deploy microservices.

## Backend layers

### Domain

`OpenMusicVideoCreator.Domain`

Owns core domain concepts and invariants and has no project references to outer layers.

Implemented domain concepts include:

- `MusicVideoProject` / `ProjectDraft`
- output aspect ratio and resolution
- generation preset choice
- project reference IDs/kinds
- media asset metadata and creation source
- `GenerationJob` / `JobAttempt`
- explicit `JobState` and `JobStateMachine`

The job state machine owns legal transitions independently from ASP.NET Core, DuckDB, provider SDKs, or worker infrastructure. Terminal work does not normally transition back to `Queued`; deliberate restart is an explicit operation.

### Application

`OpenMusicVideoCreator.Application`

Coordinates use cases and owns interfaces for external capabilities. Important seams include:

- `IProjectRepository`
- `IApplicationSettingsRepository`
- `IProjectSettingsRepository`
- `IMediaAssetRepository`
- `IApplicationPersistence`
- `IMediaStorage`
- `IProviderCatalog`
- `ICredentialResolver`
- provider capability interfaces
- `IJobRepository`
- `IJobQueue`
- `IJobExecutionDispatcher`
- `IJobChangePublisher`
- `IJobChangeStream`
- `IRenderEngine`

`ProjectService` owns project CRUD plus versioned portable JSON export/import. `ProviderSettingsService` validates provider/model settings. `JobService` owns durable job lifecycle, dependencies, pause/resume/retry/restart/cancel, retry scheduling, provider-failure mapping, and startup reconciliation. `JobProcessor` performs claim → dispatch → persisted result/failure coordination.

Application references Domain only.

### Provider capability contracts

Application defines separate capability interfaces rather than a vendor-shaped provider interface:

- `ITextGenerationProvider`
- `IImageGenerationProvider`
- `IImageEditingProvider`
- `IVideoGenerationProvider`
- `IImageToVideoProvider`
- `IVideoToVideoProvider`
- `ILipSyncProvider`
- `IUpscaleProvider`
- `ITranscriptionProvider`
- `IVisionEvaluationProvider`
- `IDirectorProvider`

Provider requests/results use application-owned records. Vendor SDK types must remain inside future provider adapters.

`ProviderModelDescriptor` carries capabilities such as references, start/end frames, seeds, negative prompts, native audio, duration options, aspect ratios, resolutions, and reference limits. Consumers query descriptors instead of inferring support from provider/model names.

### Infrastructure

`OpenMusicVideoCreator.Infrastructure`

Current concrete adapters include:

- `DuckDbDatabase`
- `DuckDbProjectRepository`
- `DuckDbSettingsRepository`
- `DuckDbMediaAssetRepository`
- `DuckDbJobRepository`
- `LocalMediaStorage`
- `MockProviderCatalog`
- `CredentialResolver`
- `MockDirectorProvider`
- `MockImageProvider`
- `MockVideoProvider`
- `MockJobExecutionDispatcher`
- `JobChangeHub`

DuckDB access uses `DuckDB.NET.Data.Full`. Operations open short-lived database connections rather than treating process memory as authoritative state.

`DuckDbJobRepository` persists jobs, dependencies, attempts, claim owner/expiry, scheduling, provider task IDs, normalized errors, and cost metadata. A short process-local claim gate serializes the candidate-selection/update transaction for the current one-process deployment. The lock is only a concurrency aid; the persisted row remains authoritative.

`JobChangeHub` is an in-memory broadcast/wakeup mechanism only. It contains no durable job state.

Infrastructure references Application and depends inward on its abstractions.

### API host

`OpenMusicVideoCreator.Api`

Owns HTTP transport and process hosting. It currently provides:

- `/healthz`
- `/api/system/version`
- project CRUD and portable import/export endpoints
- provider catalog/settings endpoints
- durable job list/create/control/attempt/dependency endpoints
- job SSE stream
- Development OpenAPI endpoint
- local-development CORS
- JSON console logging
- `X-Correlation-ID`
- infrastructure composition and DuckDB initialization
- `PersistentJobWorker`

Endpoints map transport DTOs to Application/Domain inputs and remain free of DuckDB SQL, filesystem logic, provider SDK calls, and secret resolution.

## Dependency direction

Allowed compile-time direction:

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

Forbidden directions include:

- Domain → Application / Infrastructure / API
- Application → Infrastructure / API
- Infrastructure → API

`OpenMusicVideoCreator.ArchitectureTests` checks these assembly-reference rules.

## Frontend

The frontend is a Next.js 16 / React 19 TypeScript application.

Current responsibilities remain intentionally small:

- application shell
- bootstrap backend call
- typed provider-catalog client
- typed persisted-job list client
- committed OpenAPI TypeScript snapshot for project/provider/job endpoints

The project wizard, generation queue, timeline, and editor UIs belong to later PLAN blocks. Persisted backend state remains authoritative.

## API contract strategy

ASP.NET Core OpenAPI is the source contract.

`openapi-typescript` generates TypeScript types into:

```text
frontend/src/api/schema.d.ts
```

The committed snapshot lets a fresh frontend build without a running backend. Public enums serialize as readable strings. Frontend request/response code derives types from the schema rather than maintaining parallel handwritten models.

## Logging and correlation

The API writes structured JSON console logs through standard .NET logging abstractions.

Every request receives `X-Correlation-ID`:

- incoming values are preserved
- otherwise a trace/generated ID is used
- the value is returned in the response
- the value enters the logging scope

Future project/job/scene/provider operations should add domain identifiers to scopes without logging credentials or sensitive payloads.

## Configuration

Safe defaults live in `appsettings.json`; deployment overrides use environment variables.

Current relevant keys:

```text
Storage:DatabasePath
Storage:ProjectsRoot
Jobs:WorkerEnabled
```

Environment-variable equivalents use ASP.NET Core double underscores.

`Jobs:WorkerEnabled` defaults to `true`. Integration-test hosts disable/remove the hosted worker and drive `JobProcessor` explicitly so state-machine tests remain deterministic.

## Credential references

Provider settings persist `CredentialReference`, never credential values.

Stable kinds:

- `Environment`
- `OperatingSystem`
- `External`

The built-in resolver currently implements environment references. OS/external references are stable extension seams for later secret-store adapters. `ResolvedCredential` masks `ToString()` and clears its mutable character buffer on disposal.

## Persistence

DuckDB is the authoritative metadata store.

Schema version **2** contains:

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
```

Schema v1 established projects/settings/media. Schema v2 adds persistent asynchronous execution.

Project updates replace their own mutable project metadata/collections without silently deleting media assets. Provider settings persist only credential references.

### Job persistence

The `jobs` row stores:

- job/project/scene/parent identifiers
- type and payload JSON
- provider/model IDs
- current state and pause resume-state
- priority
- attempt and automatic retry counts/max retries
- created/updated/next-run/started/completed timestamps
- provider task ID
- normalized error code/message
- estimated/actual cost and currency
- claim owner and lease expiry

`job_dependencies` stores the durable dependency graph. `job_attempts` stores immutable attempt numbers with their changing completion/result metadata.

Job updates use the expected persisted state as an optimistic concurrency condition. Worker claiming changes a `Queued` job to `Submitting`, increments the attempt number, records a lease, and inserts its attempt in one transaction.

## Media storage

Large audio/image/video bytes do not live in DuckDB.

Default layout:

```text
projects/{project-id}/
  source/
  references/
    characters/
    styles/
    locations/
  analysis/
  keyframes/
  generated/
  proxies/
  renders/
```

`LocalMediaStorage` accepts controlled storage areas and safe leaf names, creates unique names, writes under the configured root, calculates SHA-256, and returns relative metadata. `media_assets` stores metadata only.

## Portable project documents

`ProjectService` exports a versioned portable JSON document containing supported project metadata/reference IDs. It is interchange/backup data, not a second runtime source of truth. Provider settings/credentials and runtime jobs are not included in portable project metadata.

## Provider catalog and settings

`IProviderCatalog` is the provider/model discovery boundary. The current `MockProviderCatalog` exposes:

```text
mock-director
mock-image
mock-video
```

`ProviderSettingsService` validates default models and allowed operations against the current catalog. Settings include enabled state, credential reference, default models, concurrency, timeout, retries, allowed operations, and priority/fallback priority.

The API never resolves or returns secret values.

## Normalized provider failures

Provider adapters return `ProviderResult<T>` with normalized usage/failure information. Failure codes include:

- rate limited
- provider unavailable
- quota exhausted
- insufficient credits
- authentication failed
- moderation rejected
- invalid parameters
- unsupported capability
- network failure
- timeout
- transient failure
- permanent failure

The job layer maps these to operational states rather than treating every exception identically.

## Persistent job engine

The explicit persisted states are:

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

### Dependencies

A job created with incomplete dependencies starts as `WaitingForDependency`. Maintenance promotes it to `Queued` only when every dependency is `Completed`. A dependency that terminates unsuccessfully moves the dependent to `FailedPermanent` with a dependency error.

### Claiming and attempts

`PersistentJobWorker` repeatedly asks `JobProcessor` for work. The repository safely claims the next eligible `Queued` job and creates a numbered attempt. Two local worker calls against the same repository cannot both claim the same job.

Attempt count and automatic retry count are distinct. A new provider/local execution increments attempts; retry count tracks bounded automatic retry scheduling.

### Retry and provider waits

Rate limits/network/timeouts/transient failures schedule bounded retries with provider `RetryAfter` where available or exponential fallback. Quota/credit failures become `WaitingForQuota`; provider outages become `WaitingForProvider`; moderation becomes `Rejected`; invalid/auth/unsupported/permanent failures become `FailedPermanent`.

A persisted provider task ID changes safety semantics: after restart the job moves to `WaitingForProvider` for reconciliation rather than blindly submitting another paid request. Manual `retry` is rejected while such a provider task ID exists. Explicit `restart` is the deliberate destructive execution decision that clears provider-task state and creates a later new attempt.

### Pause/resume/cancel/restart

Controls exist at job, project, and project+scene scope.

- `pause` preserves a safe resume target
- `resume` never restarts a completed job
- `retry` handles retryable/waiting states but will not duplicate known provider-side work
- `restart` is explicit and can requeue terminal work
- `cancel` terminates the current job state and attempt

Successful completed work therefore remains untouched by normal resume/recovery.

### Startup recovery

On worker startup, active local work with no provider task ID is moved into bounded retry scheduling. Active work that already has a provider task ID enters `WaitingForProvider`. This makes process restart recovery explicit instead of silently assuming a remote request succeeded or failed.

### SSE

`GET /api/jobs/events` provides live notifications. `JobChangeHub` only carries job IDs; the endpoint reloads the durable job from `JobService` before serializing an event. Browser disconnect/reconnect therefore does not lose authoritative job state.

## Rendering

No FFmpeg render process is implemented yet.

`IRenderEngine` is the Application boundary. Concrete FFmpeg/ffprobe execution belongs to later media/render blocks and must use typed process arguments rather than shell-string interpolation.

## Tests

Current automated coverage includes:

- frontend typed contract checks for system/project/provider/job APIs
- backend architecture dependency tests
- health/version API tests
- project CRUD/export/import
- real temporary DuckDB project/settings/media round trips
- media path traversal and non-destructive reference updates
- provider catalog/settings/credential non-leakage/mock failure modes
- legal/illegal job transitions
- persisted job/dependency/attempt restart round trip
- duplicate-worker claim protection
- job pause/resume/cancel/restart semantics
- `WaitingForQuota` restart/resume on the same dependency graph
- startup recovery for local work and provider-task reconciliation
- provider failure → job-state normalization
- job HTTP controls
- job change-hub broadcast behavior

Paid providers are not required for normal tests.

GitHub Actions validates frontend install/lint/typecheck/tests/build, builds backend layers independently, compiles both test projects, runs granular suites, and publishes linked direct-`main` commit statuses including `ci/combined`.
