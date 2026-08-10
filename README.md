# OpenMusicVideoCreator

OpenMusicVideoCreator is an AI-assisted music-video studio built around an editable, resumable workflow:

**Song → Analysis → Storyboard → Keyframes → Animated clips → Review/regenerate → Final video**

The product specification is `AI_Music_Video_Studio_Master_Prompt.md`.

- `PLAN.md` tracks **implemented vs unfinished product work**.
- `TESTPLAN.md` tracks **validation that still needs to be executed locally**.
- `AGENTS.md` defines the direct-`main`, no-PR/no-GitHub-Actions-by-default development workflow.

## Current implementation

Implemented through PLAN Block 8:

- Next.js 16 + React 19 + TypeScript frontend
- ASP.NET Core .NET 10 modular backend: Domain, Application, Infrastructure, API
- DuckDB authoritative metadata persistence, currently schema version **5**
- filesystem media storage with SHA-256 metadata and path-traversal protection
- desktop-first Simple Mode project dashboard/editor
- project create/reopen/edit/delete and portable JSON import/export
- song upload/attachment with non-destructive replacement
- local ffprobe metadata extraction
- streaming FFmpeg waveform/energy analysis
- beat/BPM detection, derived bars/phrases/quiet ranges, heuristic vocal/instrumental estimate
- immutable/versioned song analyses and editable Structure Map
- provider-neutral transcription-assisted lyric timing that preserves supplied lyrics exactly
- reusable Character, Style, Location, and Asset Libraries
- cross-project references by stable library ID rather than copied metadata
- character appearance/forbidden changes/outfits/default continuity locks
- style prompt/camera/lighting/animation characteristics
- location environment/constraints/lighting/weather/time-of-day characteristics
- global visual reference assets with tags, favorites, source tracking, and FFmpeg-generated PNG previews
- project-specific character outfit/continuity locks plus normalized initial state values
- reference-aware deletion: referenced library items/assets cannot be silently removed
- provider-independent AI capability contracts, safe credential references, and offline mock Director/Image/Video providers
- persistent asynchronous job state, dependencies, attempts, retry/wait/recovery semantics, and SSE status updates
- versioned AI Director planning with all nine Simple/Advanced creative controls
- editable Visual Arc persisted against an exact song-analysis version
- music-aware storyboard boundaries with a typical 3-minute target of roughly 20–35 non-rigid scenes
- structured storyboard scene details for song section/lyric, purpose, emotion, composition, camera, lighting, environment motion, symbolism, continuity, and reusable Character/Style/Location references
- scene editing and reordering that creates new storyboard versions while preserving timing/provenance constraints
- separate Director Intent and expanded Final Provider Prompt
- prompt template/version history and prompt-only regeneration without starting paid generation
- exact prompt/storyboard/Visual-Arc/song-analysis provenance retained for downstream generated variants
- typed frontend contracts for projects, analysis, libraries, Director planning, providers, and jobs
- repository-side automated test code for the implemented domains and critical invariants

Keyframe/video generation workflows, deterministic final rendering, Advanced Editor, QA/routing/cost controls, and release hardening remain in later PLAN blocks. Some Block 9 persistence/service groundwork already exists, but Block 9 remains unfinished in `PLAN.md`.

## Prerequisites

- Node.js 22+
- npm 10+
- .NET 10 SDK
- Git
- **FFmpeg + ffprobe** available on `PATH`

FFmpeg/ffprobe are runtime requirements for song analysis and visual-reference preview generation.

## Install

```bash
npm install
dotnet restore backend/OpenMusicVideoCreator.sln
```

## Run locally

Start the complete development app (backend + frontend):

```powershell
./scripts/run.ps1
```

The script opens `http://localhost:3000` once both services are ready. Press `Ctrl+C` to stop them. Use `-NoBrowser` to leave the browser closed, or pass `-BackendUrl` and `-FrontendPort` to choose different local ports.

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

`NEXT_PUBLIC_API_BASE_URL` defaults to `http://localhost:5100`.

## Simple Mode

Simple Mode currently provides:

- persistent project sidebar/list
- new project, reopen, edit, save, delete
- song upload and authoritative supplied lyrics
- storyline / meaning / visual direction / mood / genre
- reusable Character / Style / Location selection
- project-specific Character continuity/outfit/state settings
- 16:9 / 9:16 / 1:1 output and target platform
- Fast / Balanced / Best Quality / Cheapest / Custom presets
- estimated and maximum budget
- offline/error/loading/empty states
- responsive and keyboard-focus behavior

Provider IDs, model IDs, seeds, raw provider JSON, and provider-specific controls remain outside Simple Mode.

## Song analysis

After a saved project has a song attached, **Analyze song** runs locally:

1. `ffprobe` reads duration, codec, sample rate, channels, and bitrate.
2. FFmpeg streams audio as mono 8 kHz signed 16-bit PCM.
3. The backend builds bounded waveform buckets and normalized energy windows without retaining the complete decoded song in application memory.
4. Signal analysis produces beat candidates and a BPM estimate when sufficiently confident.
5. Beats derive four-beat bars and four-bar phrase windows.
6. Low-energy windows derive quiet ranges.
7. Energy and zero-crossing characteristics produce a deliberately low-confidence vocal/instrumental activity estimate; uncertain input may return no estimate.
8. Energy changes propose editable Structure Map sections.
9. The analysis is persisted as an immutable DuckDB version.

Saving Structure Map edits creates another analysis version rather than overwriting history.

### Lyrics timing

Supplied lyrics remain authoritative. Optional transcription data is normalized to timestamped segments and aligned to the existing lyric lines. The alignment stores:

- exact supplied lyric text
- start/end suggestions when matched
- confidence
- supplied-lyrics SHA-256
- exact source media asset and song-analysis IDs
- independent timing version

Transcription output never replaces the project lyric text automatically.

### Analysis API

```text
GET  /api/projects/{projectId}/analysis/
POST /api/projects/{projectId}/analysis/
GET  /api/projects/{projectId}/analysis/versions
PUT  /api/projects/{projectId}/analysis/sections
GET  /api/projects/{projectId}/analysis/lyrics/timing
POST /api/projects/{projectId}/analysis/lyrics/timing
GET  /api/projects/{projectId}/analysis/lyrics/timing/versions
```

## Visual Library

The Library is application-global and reusable across projects.

### Character

Stores:

- reference type
- appearance description
- forbidden changes
- outfits
- reference assets
- default identity/face/hair/body/age/wardrobe locks

Per-project Character state is stored separately so changing one project's outfit/continuity/state does not mutate the global Character.

### Style

Stores reusable:

- prompt
- camera characteristics
- lighting characteristics
- animation characteristics
- tags/favorite/reference assets

### Location

Stores reusable:

- environment description
- constraints
- lighting
- weather
- time of day
- tags/favorite/reference assets

### Asset Library

Image/video reference uploads are stored under the global library area, not a single project. Metadata includes tags, favorite status, source description, original media ID, and optional derived preview media ID.

FFmpeg creates a bounded PNG preview using typed `ProcessStartInfo.ArgumentList` invocation. Deleting an Asset Library entry never silently deletes underlying media bytes.

### Library API

```text
GET    /api/library/items
POST   /api/library/items
GET    /api/library/items/{id}
PUT    /api/library/items/{id}
DELETE /api/library/items/{id}

GET    /api/library/assets
POST   /api/library/assets
GET    /api/library/assets/{id}
PUT    /api/library/assets/{id}
DELETE /api/library/assets/{id}
GET    /api/library/assets/{id}/preview

GET /api/projects/{projectId}/characters/states/
PUT /api/projects/{projectId}/characters/states/{characterId}
```

A Character/Style/Location referenced by a project cannot be deleted until references are removed. An Asset Library entry referenced by a visual-library item is similarly protected.

## AI Director and storyboard

Director planning consumes the exact latest song analysis when a plan is created, together with project lyrics, storyline, meaning, visual direction, mood/genre, attached Characters/Styles/Locations, and project-specific Character continuity state.

The planning controls are provider-independent normalized values for:

- literal ↔ symbolic
- narrative strength
- abstraction
- emotion
- darkness ↔ warmth
- surrealism ↔ realism
- visual complexity
- acting intensity
- camera energy

The mock Director creates a versioned Visual Arc and structured storyboard without calling a paid provider. Musical section/phrase boundaries are preferred when they are close enough to the desired scene pacing; hard minimum timing prevents invalid micro-scenes.

Scene edits are non-destructive. Saving a scene creates a new storyboard version and a new prompt version for that scene only. Reordering moves scene content across the existing ordered timing slots so the storyboard remains contiguous. Prompt-only regeneration appends a prompt revision but does not create an image/video job.

Later edits deliberately preserve the storyboard's exact `SongAnalysisId` and referenced Visual Arc/controls. Creating a fresh Director plan is the operation that intentionally adopts the newest song analysis.

### Director API

```text
POST /api/projects/{projectId}/director/plan
GET  /api/projects/{projectId}/director/visual-arc
GET  /api/projects/{projectId}/director/visual-arc/versions
PUT  /api/projects/{projectId}/director/visual-arc
GET  /api/projects/{projectId}/director/storyboard
GET  /api/projects/{projectId}/director/storyboard/versions
PUT  /api/projects/{projectId}/director/storyboard/scenes/{sceneId}
POST /api/projects/{projectId}/director/storyboard/reorder
GET  /api/projects/{projectId}/director/storyboard/scenes/{sceneId}/prompts
POST /api/projects/{projectId}/director/storyboard/scenes/{sceneId}/prompts/regenerate
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

Projects store stable references to Song/Character/Style/Location IDs. They do not embed copies of reusable visual-library metadata.

## Persistence

Default layout:

```text
data/
  app.duckdb
projects/
  library/
    originals/
    previews/
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

DuckDB schema version 5 includes project/media/provider/job metadata plus:

```text
song_analyses
lyric_timing_analyses
library_assets
visual_library_items
project_character_states
```

Director Visual Arc, storyboard, and prompt history are versioned JSON records stored durably through the project-settings repository. This preserves Block 8 restart durability without adding a schema migration solely for planning history.

Large media files are never stored as DuckDB blobs.

## Provider API and credentials

```text
GET /api/providers/
GET /api/providers/{providerId}/settings
PUT /api/providers/{providerId}/settings
```

Provider settings store credential references, never plaintext secret values. Current generation providers are offline mocks suitable for normal development without paid calls.

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

Frontend request/response types derive from that schema.

## Validation workflow

Implementation completion belongs in `PLAN.md`. Unexecuted validation belongs in `TESTPLAN.md`.

Core local commands include:

```bash
npm run lint
npm run typecheck
npm run test:frontend
npm run build:frontend

dotnet build backend/OpenMusicVideoCreator.sln -c Release
dotnet test backend/OpenMusicVideoCreator.sln -c Release
```

Or:

```bash
./scripts/validate.sh
```

PowerShell:

```powershell
./scripts/validate.ps1
```

Do not infer success from implementation/source inspection. A local Codex run should execute `TESTPLAN.md` and check off only commands/scenarios that actually succeed.

## Repository structure

```text
frontend/
  app/
  src/api/
  src/features/projects/
  src/features/analysis/
  src/features/library/
  src/features/planning/
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
3. `TESTPLAN.md`
4. `AGENTS.md`
5. `ARCHITECTURE.md`
6. `SKILLS.md`

Keep modules focused, preserve provider independence, persist asynchronous state, keep user assets/generations non-destructive, work directly on `main` unless explicitly told otherwise, and never claim a validation step passed unless it actually executed.
