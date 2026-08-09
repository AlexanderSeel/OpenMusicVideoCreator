# OpenMusicVideoCreator

OpenMusicVideoCreator is an AI-assisted music-video studio designed around an editable, resumable workflow:

**Song → Analysis → Storyboard → Keyframes → Animated clips → Review/regenerate → Final video**

The product specification lives in `AI_Music_Video_Studio_Master_Prompt.md`. `PLAN.md` contains unfinished implementation work only.

## Current implementation

The repository currently contains the executable foundation plus durable project persistence:

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
- `/healthz` and `/api/system/version` bootstrap endpoints
- JSON console logging and `X-Correlation-ID` request correlation
- architecture, API, DuckDB, media-storage, and project round-trip integration tests
- GitHub Actions CI for frontend and backend; direct `main` pushes publish a `ci/combined` commit status

AI provider adapters, persistent generation jobs, FFmpeg rendering, music analysis, storyboard generation, and editor functionality remain unfinished and are tracked in `PLAN.md`.

## Prerequisites

- Node.js 22 or newer
- npm 10 or newer
- .NET 10 SDK
- Git

FFmpeg becomes a runtime prerequisite when the render/media-analysis blocks are implemented; it is not required for the current persistence foundation.

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
