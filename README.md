# OpenMusicVideoCreator

OpenMusicVideoCreator is an AI-assisted music-video studio designed around an editable, resumable workflow:

**Song → Analysis → Storyboard → Keyframes → Animated clips → Review/regenerate → Final video**

The product specification lives in `AI_Music_Video_Studio_Master_Prompt.md`. `PLAN.md` contains unfinished implementation work only.

## Current implementation

The repository currently contains the executable application foundation:

- Next.js 16 + React 19 + TypeScript frontend
- ASP.NET Core .NET 10 backend
- layered backend projects: Domain, Application, Infrastructure, API
- typed frontend/backend contract strategy based on ASP.NET OpenAPI + `openapi-typescript`
- `/healthz` and `/api/system/version` bootstrap endpoints
- JSON console logging and `X-Correlation-ID` request correlation
- architecture tests for backend dependency direction
- API integration tests using `WebApplicationFactory`
- baseline GitHub Actions CI for frontend and backend

DuckDB persistence, project CRUD, AI providers, generation jobs, FFmpeg rendering, music analysis, storyboard generation, and editor functionality are not implemented yet. They remain in `PLAN.md`.

## Prerequisites

- Node.js 22 or newer
- npm 10 or newer
- .NET 10 SDK
- Git

FFmpeg will become a runtime prerequisite when the render block is implemented; it is not required for the current foundation.

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
.github/workflows/ci.yml          baseline CI
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
