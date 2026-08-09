# OpenMusicVideoCreator

OpenMusicVideoCreator is an AI-assisted music-video studio built around an editable, resumable workflow:

**Song → Analysis → Storyboard → Keyframes → Animated clips → Review/regenerate → Final video**

The product specification is `AI_Music_Video_Studio_Master_Prompt.md`. `PLAN.md` is the checkbox-based implementation tracker.

## Current implementation

The repository currently includes:

- Next.js 16 + React 19 + TypeScript frontend
- ASP.NET Core .NET 10 modular backend: Domain, Application, Infrastructure, API
- DuckDB authoritative metadata persistence
- filesystem media storage with SHA-256 metadata and path-traversal protection
- desktop-first Simple Mode project dashboard/editor
- project create/reopen/edit/delete
- song upload/attachment with non-destructive replacement
- lyrics, storyline, meaning, visual direction, mood, genre, output target, preset, and budget inputs
- Character/Style/Location placeholders for the later reusable-library block
- local ffprobe metadata extraction
- streaming FFmpeg waveform/energy analysis
- beat candidates and BPM estimate
- derived four-beat bars, four-bar phrases, and quiet ranges
- versioned DuckDB song analyses
- visible waveform, beat/bar/phrase markers, quiet shading, and supplied-lyrics lane
- editable/versioned Structure Map section labels/types/start/end boundaries
- provider-independent AI capability contracts, safe credential references, provider settings, and offline mock Director/Image/Video providers
- persistent asynchronous job state, dependencies, attempts, retry/wait/recovery semantics, and SSE status updates
- typed frontend project/song/analysis/provider/job API contracts

Still unfinished in the current song-analysis block: vocal/instrumental classification and optional transcription-assisted lyric timing. Storyboard generation, real image/video generation adapters, asset libraries, rendering, and Advanced Editor work are tracked later in `PLAN.md`.

## Prerequisites

- Node.js 22+
- npm 10+
- .NET 10 SDK
- Git
- **FFmpeg + ffprobe** available on `PATH`

FFmpeg/ffprobe are now runtime requirements for song analysis, not just future rendering.

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

Simple Mode currently provides:

- persistent project sidebar/list
- new project, reopen, edit, save, delete
- song upload
- authoritative supplied lyrics
- storyline / meaning / visual direction
- mood / genre
- Character / Style / Location placeholders
- 16:9 / 9:16 / 1:1 output
- target platform
- Fast / Balanced / Best Quality / Cheapest / Custom presets
- estimated and maximum budget
- offline/error/loading/empty states
- responsive and keyboard-focus behavior

Provider IDs, model IDs, seeds, raw provider JSON, and provider-specific controls stay out of Simple Mode.

## Song analysis

After a saved project has a song attached, **Analyze song** runs completely locally:

1. `ffprobe` reads duration, codec, sample rate, channels, and bitrate.
2. FFmpeg streams the first audio stream as mono 8 kHz signed 16-bit PCM.
3. The backend builds bounded waveform buckets and normalized 50 ms energy points without loading the decoded song into memory.
4. Local onset candidates produce beat markers and a BPM estimate.
5. Persisted beats derive four-beat bars and four-bar phrases.
6. Low-energy windows derive quiet ranges.
7. Energy/duration changes propose editable song sections.
8. The complete analysis is persisted as a new DuckDB version.

The UI then shows:

- duration / BPM / sample rate
- waveform
- beat and bar markers
- phrase bands
- quiet shading
- supplied lyrics lane
- editable Structure Map

Saving Structure Map changes creates another analysis version. Earlier versions remain available through the API.

The original supplied lyrics are not rewritten by signal analysis.

### Analysis API

```text
GET  /api/projects/{projectId}/analysis/
POST /api/projects/{projectId}/analysis/
GET  /api/projects/{projectId}/analysis/versions
PUT  /api/projects/{projectId}/analysis/sections
```

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

A song upload validates file size/type/name, stores the binary under the project `source/` area, stores metadata in DuckDB, and points the project `Song` reference at the new asset. Replacing the song does **not** silently delete the previous media asset.

## Persistence

Default layout:

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

Current DuckDB schema version is 3 and includes `song_analyses` in addition to project/media/provider/job metadata.

Large media files are never stored as DuckDB blobs.

## Provider API and credentials

```text
GET /api/providers/
GET /api/providers/{providerId}/settings
PUT /api/providers/{providerId}/settings
```

Provider settings store credential references, never plaintext secret values. Current generation providers are offline mocks suitable for development without paid calls.

## Persistent job engine

The persisted state model includes:

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

Normal resume does not regenerate completed work. Jobs with known provider task IDs are reconciled after restart instead of blindly creating duplicate remote requests.

## Typed API contracts

ASP.NET Core OpenAPI is the source contract. The committed frontend snapshot is:

```text
frontend/src/api/schema.d.ts
```

With the backend running:

```bash
npm run api:generate --workspace frontend
```

Frontend API code derives request/response types from this schema.

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

In the current assistant execution environment the repository cannot be checked out because external repository DNS/network access is unavailable, so the complete local build/test suite has not been executed here. FFmpeg/ffprobe 7.1.5 command shapes were validated locally against a generated audio fixture. No GitHub Actions were used for this validation.

## Repository structure

```text
frontend/
  app/
  src/api/
  src/features/projects/
  src/features/analysis/
backend/
  src/
    OpenMusicVideoCreator.Domain/
    OpenMusicVideoCreator.Application/
    OpenMusicVideoCreator.Infrastructure/
    OpenMusicVideoCreator.Api/
  tests/
    OpenMusicVideoCreator.ArchitectureTests/
    OpenMusicVideoCreator.Api.Tests/
```

## Development rules

Read before implementation work:

1. `AI_Music_Video_Studio_Master_Prompt.md`
2. `PLAN.md`
3. `AGENTS.md`
4. `ARCHITECTURE.md`
5. `SKILLS.md`

Keep modules focused, preserve provider independence, persist asynchronous state, keep generated work non-destructive, and never claim a validation step passed unless it actually executed.
