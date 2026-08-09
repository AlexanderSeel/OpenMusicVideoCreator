# OpenMusicVideoCreator

OpenMusicVideoCreator is an AI-assisted music-video studio designed around an editable, resumable workflow:

**Song → Analysis → Storyboard → Keyframes → Animated clips → Review/regenerate → Final video**

The product specification lives in `AI_Music_Video_Studio_Master_Prompt.md`. `PLAN.md` is the visible checkbox-based implementation tracker.

## Current implementation

The repository currently contains the executable foundation, durable project/provider persistence, restart-safe asynchronous jobs, and the first user-facing Simple Mode workflow:

- Next.js 16 + React 19 + TypeScript frontend
- ASP.NET Core .NET 10 modular backend: Domain, Application, Infrastructure, API
- typed frontend/backend API contract based on ASP.NET OpenAPI + `openapi-typescript`
- DuckDB authoritative metadata persistence using `DuckDB.NET.Data.Full`
- durable project model, project/application settings, filesystem media storage, project CRUD, and portable project JSON
- desktop-first Simple Mode project dashboard/editor with responsive fallback
- project create/reopen/edit/delete workflow backed by the durable project API
- project inputs for title, artist, lyrics, storyline, meaning, visual direction, mood, genre, output aspect/target, preset, and budgets
- Simple/Advanced/Expert progressive-disclosure tabs; only Simple is enabled in the current block
- Character/Style/Location library placeholders without fake local implementations before Block 7
- accessible loading/error/empty/offline states, keyboard focus treatment, reduced-motion handling, and responsive layout
- durable song attachment using the existing media-storage abstraction and `ProjectReferenceKind.Song`
- song upload validation for supported audio extensions/MIME types, safe leaf filenames, non-empty content, and a 512 MB maximum
- replacing a project song creates a new media asset/reference without silently deleting the previous media asset
- provider-independent capability interfaces, capability-aware model catalog, persisted safe provider settings, credential references, normalized provider failures, and offline mock Director/Image/Video providers
- persistent jobs, attempts, dependencies, provider task IDs, retry metadata, costs, scheduling, parent/scene/project associations, and claim leases
- explicit job state machine covering queued/provider/processing/waiting/retry/rejected/permanent/paused/cancelled/completed states
- background `PersistentJobWorker` with safe one-process claiming and restart recovery
- dependency release/failure propagation, bounded retry scheduling, quota/provider waits, pause/resume/retry/restart/cancel controls at job/project/scene scope
- protection against re-submitting provider work when a persisted provider task ID already exists
- SSE job-change stream that reloads persisted state before emission; DuckDB, not the stream, is authoritative
- typed frontend project/provider/job/song contracts
- repository tests for project CRUD, song attachment semantics, provider behavior, persistent job behavior, and Simple Mode structure

Real paid AI adapters, song analysis, storyboard generation, generation-specific provider dispatch, FFmpeg rendering, reusable visual libraries, and the Advanced Editor remain unfinished and are tracked in `PLAN.md`.

## Prerequisites

- Node.js 22 or newer
- npm 10 or newer
- .NET 10 SDK
- Git

FFmpeg becomes a runtime prerequisite when the media-analysis/render blocks are implemented.

## Install

```bash
npm install
dotnet restore backend/OpenMusicVideoCreator.sln
```

## Run locally

Backend:

```bash
dotnet run --project backend/src/OpenMusicVideoCreator.Api/OpenMusicVideoCreator.Api.csproj
```

Development backend URL: `http://localhost:5100`.

Frontend:

```bash
npm run dev:web
```

Open `http://localhost:3000`.

The frontend reads `NEXT_PUBLIC_API_BASE_URL` and defaults to `http://localhost:5100`.

## Simple Mode

The current home page is the first real product workflow rather than a foundation status page.

Simple Mode provides:

- persistent project list/sidebar
- new-project workflow
- create/reopen/edit/delete
- song selection/upload
- lyrics and story/meaning/direction inputs
- mood and genre
- Character/Style/Location placeholders for Block 7
- aspect ratio and target platform
- Fast, Balanced, Best Quality, Cheapest, and Custom presets
- estimated/maximum budget
- online/offline state and API error recovery

Provider IDs, model IDs, seeds, raw provider JSON, and provider-specific controls are intentionally absent from Simple Mode.

Advanced and Expert/Custom tabs are visible as progressive-disclosure destinations but remain disabled until the corresponding implementation blocks are reached.

## Persistence and local data

Default storage:

```text
data/
  app.duckdb
projects/
  {project-id}/
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

Configure with `Storage__DatabasePath` and `Storage__ProjectsRoot` or the equivalent `Storage` section.

DuckDB stores structured metadata only. Large audio/image/video data remains on filesystem/object storage. Media metadata includes location, SHA-256, MIME type, dimensions, duration, size, source, and timestamps.

## Project API

```text
GET    /api/projects/
POST   /api/projects/
GET    /api/projects/{id}
PUT    /api/projects/{id}
DELETE /api/projects/{id}
GET    /api/projects/{id}/song
POST   /api/projects/{id}/song
GET    /api/projects/{id}/export
POST   /api/projects/import
```

Portable project JSON is versioned interchange/backup data; DuckDB remains runtime-authoritative. Project/reference edits do not silently delete generated assets.

### Song attachment

A successful upload:

1. validates size, extension, MIME type, and filename safety,
2. stores the binary in the project `source/` area,
3. persists SHA-256 and other media metadata in DuckDB,
4. replaces the project’s `Song` reference with the new media asset ID.

The previous media asset is intentionally retained. Later cleanup/asset-management logic can remove unreferenced assets explicitly instead of losing user work implicitly.

## Provider API and credentials

```text
GET /api/providers/
GET /api/providers/{providerId}/settings
PUT /api/providers/{providerId}/settings
```

Provider settings persist credential references, never secret values. Example:

```json
{
  "kind": "Environment",
  "identifier": "OPENAI_API_KEY"
}
```

The current built-in resolver supports environment references. `OperatingSystem` and `External` reference kinds are stable extension seams for later secret-store adapters. Current registered providers are offline mocks and require no API keys.

## Persistent job engine

Job states are persisted in DuckDB and survive backend restarts. The current state model includes:

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

A completed/rejected/permanent/cancelled job is terminal. Normal resume does not regenerate completed work; a deliberate `restart` action is required to create a new attempt.

Jobs may depend on other jobs. Dependents stay `WaitingForDependency` until prerequisites complete; a failed terminal dependency moves the dependent to a permanent dependency failure.

Provider failures are normalized into quota/provider waits, bounded scheduled retries, rejection, retryable failure, or permanent failure. A job with a persisted provider task ID is reconciled after restart rather than blindly resubmitted, reducing duplicate paid requests.

The worker is enabled by default:

```json
{
  "Jobs": {
    "WorkerEnabled": true
  }
}
```

Tests disable/remove the hosted worker and drive `JobProcessor` deterministically.

### Job API

```text
GET  /api/jobs/
POST /api/jobs/
GET  /api/jobs/{id}
GET  /api/jobs/{id}/attempts
GET  /api/jobs/{id}/dependencies
POST /api/jobs/{id}/pause
POST /api/jobs/{id}/resume
POST /api/jobs/{id}/retry
POST /api/jobs/{id}/restart
POST /api/jobs/{id}/cancel
POST /api/jobs/projects/{projectId}/pause|resume|cancel
POST /api/jobs/projects/{projectId}/scenes/{sceneId}/pause|resume|cancel
GET  /api/jobs/events
```

`/api/jobs/events` is an SSE notification stream. Notifications contain freshly reloaded persisted job state; the stream itself is not durable state.

## Typed API contracts

ASP.NET Core OpenAPI is the source contract. The frontend snapshot is committed at:

```text
frontend/src/api/schema.d.ts
```

With the backend running, regenerate it using:

```bash
npm run api:generate --workspace frontend
```

Frontend API code derives request/response types from this schema instead of maintaining parallel DTO definitions.

## Validate

Linux/macOS:

```bash
./scripts/validate.sh
```

PowerShell:

```powershell
./scripts/validate.ps1
```

Core commands:

```bash
npm run lint
npm run typecheck
npm run test:frontend
npm run build:frontend

dotnet build backend/OpenMusicVideoCreator.sln -c Release
dotnet test backend/OpenMusicVideoCreator.sln -c Release
```

## Repository structure

```text
frontend/
  app/                            Next.js routes + design tokens/styles
  src/api/                        typed HTTP client + schema snapshot
  src/features/projects/          Simple Mode project feature
backend/
  src/
    OpenMusicVideoCreator.Domain/
    OpenMusicVideoCreator.Application/
    OpenMusicVideoCreator.Infrastructure/
    OpenMusicVideoCreator.Api/
  tests/
    OpenMusicVideoCreator.ArchitectureTests/
    OpenMusicVideoCreator.Api.Tests/
scripts/                          repo validation + agent-skill helpers
```

## Development rules

Read before implementation work:

1. `AI_Music_Video_Studio_Master_Prompt.md`
2. `PLAN.md`
3. `AGENTS.md`
4. `ARCHITECTURE.md`
5. `SKILLS.md`

Core rules include modular reusable code, provider-independent business logic, persisted asynchronous generation, non-destructive asset versioning, bounded retries/cost, and restart-safe continuation.
