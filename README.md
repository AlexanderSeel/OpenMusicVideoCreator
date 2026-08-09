# OpenMusicVideoCreator

OpenMusicVideoCreator is an AI-assisted music-video studio designed around an editable, resumable workflow:

**Song → Analysis → Storyboard → Keyframes → Animated clips → Review/regenerate → Final video**

The product specification lives in `AI_Music_Video_Studio_Master_Prompt.md`. `PLAN.md` contains unfinished implementation work only.

## Current implementation

The repository currently contains the executable foundation, durable project persistence, and provider-independent AI seams:

- Next.js 16 + React 19 + TypeScript frontend
- ASP.NET Core .NET 10 backend
- layered backend projects: Domain, Application, Infrastructure, API
- typed frontend/backend contract strategy based on ASP.NET OpenAPI + `openapi-typescript`
- DuckDB metadata persistence using `DuckDB.NET.Data.Full`
- durable project model for title, artist, lyrics, storyline/meaning/direction, mood, genre, output target, preset, budgets, and reusable-reference IDs
- application and project settings repositories
- filesystem media storage with SHA-256 metadata, path-traversal protection, and deterministic per-project directories
- media metadata stored in DuckDB while audio/image/video bytes stay outside the database
- project CRUD API plus portable project JSON export/import
- provider capability interfaces for text, image, image editing, video, image-to-video, video-to-video, lip sync, upscale, transcription, vision evaluation, and Director planning
- provider/model catalog with capability metadata instead of global hard-coded model assumptions
- persisted provider settings for enabled state, credential reference, default models, concurrency, timeout, retries, allowed operations, priority, and fallback priority
- environment credential resolution with opaque reference kinds reserved for OS/external secret-store adapters; resolved secret values are never returned by the API or persisted
- normalized provider result/failure contracts covering rate limit, quota, credits/auth, rejection, invalid parameters, unsupported capability, network, timeout, transient, and permanent failures
- offline `MockDirectorProvider`, `MockImageProvider`, and `MockVideoProvider` with controllable success/delay/failure scenarios
- `/healthz` and `/api/system/version` bootstrap endpoints
- JSON console logging and `X-Correlation-ID` request correlation
- architecture, API, DuckDB, media-storage, project round-trip, and provider subsystem integration tests
- GitHub Actions CI for frontend and backend; direct `main` pushes publish detailed commit statuses plus `ci/combined`

Real paid AI provider adapters, persistent generation jobs, FFmpeg rendering, music analysis, storyboard generation, and editor functionality remain unfinished and are tracked in `PLAN.md`.

## Prerequisites

- Node.js 22 or newer
- npm 10 or newer
- .NET 10 SDK
- Git

FFmpeg becomes a runtime prerequisite when the render/media-analysis blocks are implemented; it is not required for the current persistence/provider foundation.

## Install

From the repository root:

```bash
npm install

dotnet restore backend/OpenMusicVideoCreator.sln
```

## Run locally

Start the backend:

```bash
dotnet run --project backend/src/OpenMusicVideoCreator.Api/OpenMusicVideoCreator.Api.csproj
```

The development launch profile listens on:

```text
http://localhost:5100
```

Start the frontend in a second terminal:

```bash
npm run dev:web
```

Open:

```text
http://localhost:3000
```

The frontend reads `NEXT_PUBLIC_API_BASE_URL` and defaults to `http://localhost:5100`. Copy `frontend/.env.example` to `frontend/.env.local` only when you need to override it.

## Persistence and local data

Default backend storage:

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

Configure these paths through:

```text
Storage__DatabasePath
Storage__ProjectsRoot
```

or the equivalent `Storage` section in `appsettings.json`.

DuckDB is authoritative for running application metadata. Large media blobs are never stored in DuckDB. Stored media receives a unique file name and SHA-256 checksum, while DuckDB records only metadata such as location, checksum, MIME type, dimensions, duration, size, source, and timestamps.

## Project API

Current project endpoints:

```text
GET    /api/projects/
POST   /api/projects/
GET    /api/projects/{id}
PUT    /api/projects/{id}
DELETE /api/projects/{id}
GET    /api/projects/{id}/export
POST   /api/projects/import
```

Deleting or changing project reference metadata does not silently delete generated media assets. Media deletion is an explicit storage operation.

The export endpoint returns portable versioned project JSON. It is useful for interchange/backup, but DuckDB remains authoritative while the application is running.

## Provider API and credentials

Current provider endpoints:

```text
GET /api/providers/
GET /api/providers/{providerId}/settings
PUT /api/providers/{providerId}/settings
```

The provider catalog reports model capabilities such as references, start/end frames, seed, negative prompts, native audio, duration options, aspect ratios, resolutions, and reference limits. Frontend code can therefore query capabilities rather than embedding model assumptions.

Provider settings persist only a credential **reference**. Example environment reference:

```json
{
  "kind": "Environment",
  "identifier": "OPENAI_API_KEY"
}
```

The value of `OPENAI_API_KEY` is resolved only inside the credential resolver when a provider operation needs it. It is not stored in DuckDB, project exports, API responses, or logs. `OperatingSystem` and `External` reference kinds are part of the stable contract for later secret-store adapters; the current built-in resolver implements environment references only.

The currently registered providers are offline mocks. They require no API keys and are intended for tests and development of later generation/job workflows.

## Typed API contracts

ASP.NET Core exposes OpenAPI in the Development environment. The frontend keeps a generated TypeScript contract snapshot in `frontend/src/api/schema.d.ts`.

With the backend running locally, regenerate it with:

```bash
npm run api:generate --workspace frontend
```

Frontend API code should derive request/response types from that generated schema rather than duplicating DTO shapes by hand.

## Validate

Linux/macOS:

```bash
./scripts/validate.sh
```

PowerShell:

```powershell
./scripts/validate.ps1
```

Or run the main commands individually:

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
frontend/                         Next.js UI
backend/
  src/
    OpenMusicVideoCreator.Domain/
    OpenMusicVideoCreator.Application/
    OpenMusicVideoCreator.Infrastructure/
    OpenMusicVideoCreator.Api/
  tests/
    OpenMusicVideoCreator.ArchitectureTests/
    OpenMusicVideoCreator.Api.Tests/
.github/workflows/ci.yml          baseline CI + direct-main status
scripts/                          repo validation + agent-skill helpers
```

## Development rules

Read before implementation work:

1. `AI_Music_Video_Studio_Master_Prompt.md`
2. `PLAN.md`
3. `AGENTS.md`
4. `ARCHITECTURE.md`
5. `SKILLS.md`

Core rules include modular reusable code, provider-independent business logic, persisted asynchronous generation, non-destructive asset versioning, bounded retries/cost, and keeping successful work resumable across application restarts.
