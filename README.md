# OpenMusicVideoCreator

OpenMusicVideoCreator is an AI-assisted music-video studio built around an editable, resumable workflow:

**Song → Analysis → Storyboard → Keyframes → Animated clips → Review/regenerate → Final video**

The product specification is `AI_Music_Video_Studio_Master_Prompt.md`.

- `PLAN.md` tracks **implemented vs unfinished product work**.
- `TESTPLAN.md` tracks **validation that still needs to be executed locally**.
- `AGENTS.md` defines the repository development workflow.

## Current implementation

Repository-side implementation now covers Blocks 1–8 plus the **offline/mock paths of Blocks 9 and 10**. Real image/video provider integrations remain deliberately open until the complete mock validation matrix in `TESTPLAN.md` has actually passed.

Implemented capabilities include:

- Next.js 16 + React 19 + TypeScript frontend
- ASP.NET Core .NET 10 modular backend: Domain, Application, Infrastructure, API
- DuckDB authoritative structured persistence plus local filesystem media storage
- project create/reopen/edit/delete and portable JSON import/export
- non-destructive song upload/replacement
- ffprobe metadata plus streaming FFmpeg waveform/energy analysis
- beat/BPM, bars, phrases, quiet ranges, section suggestions, and editable Structure Map
- authoritative supplied lyrics plus optional timestamp alignment
- reusable Character, Style, Location, and Asset Libraries
- project-specific Character outfit/continuity/state
- provider-independent capability contracts and credential references
- offline mock Director/Image/Video providers
- persistent asynchronous job engine with dependencies, retries, waiting states, provider task IDs, startup recovery, and SSE updates
- versioned AI Director, Visual Arc, storyboard, scene editing, and prompt history
- Start/End keyframe generation with immutable prompt provenance
- continuity-aware Character/Style/Location reference routing with provider reference limits
- non-destructive keyframe variants, compare/select/delete/regenerate, per-scene settings, and approval before animation
- scene image-to-video generation from approved keyframes
- non-destructive animated clip variants with prompt/keyframe/job/provider/model/cost provenance
- capability-aware duration/aspect/resolution/end-frame validation
- optional compatible-provider fallback; Custom mode can disable fallback
- generated clip persistence and browser preview endpoints
- global Generation Queue UI driven by the persisted job list plus SSE notifications
- job pause/resume/retry/restart/cancel plus project/scene scope controls
- Simple Mode automatic routing with provider-specific controls hidden; Advanced/Custom progressively expose supported settings

See:

- `docs/BLOCK9_KEYFRAME_GENERATION.md`
- `docs/BLOCK10_VIDEO_GENERATION.md`

Deterministic final rendering, full Advanced timeline editing, QA/smart routing/cost caps, release hardening, and real-provider integrations remain in later/open PLAN work.

## Prerequisites

- Node.js 22+
- npm 10+
- .NET 10 SDK
- Git
- **FFmpeg + ffprobe** on `PATH`

FFmpeg/ffprobe are runtime requirements for song analysis and visual-reference previews. Block 11 will extend the deterministic FFmpeg boundary to final rendering.

## Install

```bash
npm install
dotnet restore backend/OpenMusicVideoCreator.sln
```

## Run locally

Start backend + frontend:

```powershell
./scripts/run.ps1
```

Use `-NoBrowser` if the script should not open `http://localhost:3000` automatically.

Backend only:

```bash
dotnet run --project backend/src/OpenMusicVideoCreator.Api/OpenMusicVideoCreator.Api.csproj
```

Default development backend: `http://localhost:5100`.

Frontend only:

```bash
npm run dev:web
```

`NEXT_PUBLIC_API_BASE_URL` defaults to `http://localhost:5100`.

## Product modes

### Simple

Simple Mode keeps provider internals out of the primary workflow. It provides project/song/library/planning/generation controls while automatically routing supported mock/provider capabilities.

Provider IDs, model IDs, seeds, negative prompts, raw provider JSON, and provider-specific animation controls are hidden from Simple Mode.

### Advanced / Expert Custom

Advanced/Custom progressively expose capability-supported generation settings. Current keyframe/video controls include provider/model selection, supported resolution/duration, optional End-frame use, seed/negative prompt where supported, and Custom fallback policy.

Unsupported settings are rejected by the backend rather than merely hidden by the frontend.

## Song analysis and Structure Map

After a saved project has a Song attached, local analysis:

1. Uses `ffprobe` for authoritative duration/codec/sample-rate/channel/bitrate metadata.
2. Streams audio through FFmpeg as bounded PCM analysis input.
3. Builds waveform and normalized energy data.
4. Estimates beat candidates/BPM when sufficiently confident.
5. Derives bars, phrase windows, quiet ranges, and low-confidence vocal/instrumental activity.
6. Proposes editable Structure Map sections.
7. Persists immutable analysis versions.

Saving Structure Map edits creates a new version instead of overwriting history.

Supplied lyrics remain authoritative. Optional transcription segments only contribute timing/confidence metadata and never silently rewrite the project lyrics.

## Reusable visual library

The application-global Library stores stable reusable Character, Style, Location, and Asset IDs.

Character data supports appearance, forbidden changes, outfits/reference assets, and default identity/face/hair/body/age/wardrobe locks. Project-specific outfit/continuity/state is persisted separately.

Style data stores prompt, camera, lighting, animation characteristics, tags/favorite/reference assets. Location data stores environment, constraints, lighting, weather, time of day, tags/favorite/reference assets.

Referenced library items/assets cannot be silently deleted. Underlying user media is not automatically destroyed when metadata/index entries change.

## AI Director and storyboard

Director planning combines the exact song-analysis version, supplied lyrics, project story/meaning/visual direction/mood/genre, attached visual-library references, project Character state, and normalized Director controls.

The result is a versioned editable Visual Arc plus a structured storyboard. Scenes retain song section/lyric, purpose, emotion, action, composition, camera, lighting, environment/motion, symbolism, continuity requirements, and stable visual-library IDs.

Scene edits/reordering create new storyboard/prompt versions. Director Intent remains separate from the Final Provider Prompt. Prompt-only regeneration never automatically starts image/video generation.

Downstream keyframes/clips reference immutable `PromptVersionId` values rather than copying unauditable prompt text.

## Keyframe generation

Each scene can create a Start keyframe and optional End keyframe through the persistent job engine.

The coordinator:

- resolves an enabled image-generation capability/model
- validates provider/model settings
- loads the selected immutable prompt version
- prioritizes Character outfit/base references, then Style/Location references
- caps references at the provider model's `MaxReferences`
- persists a planned variant before enqueueing the job
- materializes generated provider output into local keyframe media
- records provider/model/job/media/cost provenance

Regeneration appends variants. Selecting a new completed variant changes only the selection reference; older successful variants remain intact.

Animation requires approval of the current completed Start selection and optional End selection.

## Scene video generation

Approved keyframes can be animated via `scene.video.generate` jobs.

The video coordinator:

- routes `ImageToVideo`-capable models with Start-frame support
- validates optional End-frame support
- resolves the nearest supported duration to the storyboard scene
- preserves project aspect ratio and supported resolution
- stores dependencies on approved keyframe jobs
- enqueues without blocking HTTP on provider work
- materializes successful video output into generated project media
- persists a non-destructive `SceneClipVariant`

### Fallback

When enabled, fallback candidates are included only if they can preserve the exact Start/End-frame, duration, aspect-ratio, and resolution semantics resolved for the primary request.

Fallback is permitted for operational/provider failures such as quota/credits, rate limit, outage, authentication, unsupported adapter capability, network, timeout, and transient failure. Moderation rejection, invalid parameters, and permanent failures do not silently switch providers.

If a fallback succeeds, the clip variant records the provider/model that actually produced the asset. Custom mode can disable fallback.

## Persistent Generation Queue

The frontend Generation Queue first reads persisted jobs from `GET /api/jobs/` and then consumes `/api/jobs/events` via `EventSource`.

SSE is only a notification mechanism; DuckDB jobs remain authoritative. Reconnect causes persisted state to be reloaded rather than assuming all in-memory events were received.

Queue rows expose:

- job type
- state
- elapsed time
- attempts/retries
- estimated/actual cost
- next-run scheduling
- errors
- provider/model in Advanced/Custom mode
- pause/resume/retry/restart/cancel actions

Existing project and scene scope actions use the same persisted job graph.

## API overview

### Projects / analysis / library / Director

```text
/api/projects/...
/api/projects/{id}/song
/api/projects/{projectId}/analysis/...
/api/projects/{projectId}/analysis/lyrics/timing...
/api/library/items...
/api/library/assets...
/api/projects/{projectId}/characters/states...
/api/projects/{projectId}/director/...
```

### Keyframes

```text
GET    /api/projects/{projectId}/scenes/{sceneId}/keyframes/
GET    /api/projects/{projectId}/scenes/{sceneId}/keyframes/settings
PUT    /api/projects/{projectId}/scenes/{sceneId}/keyframes/settings
POST   /api/projects/{projectId}/scenes/{sceneId}/keyframes/generate
GET    /api/projects/{projectId}/scenes/{sceneId}/keyframes/{variantId}/preview
POST   /api/projects/{projectId}/scenes/{sceneId}/keyframes/{variantId}/select
DELETE /api/projects/{projectId}/scenes/{sceneId}/keyframes/{variantId}
GET    /api/projects/{projectId}/scenes/{sceneId}/keyframes/approval
POST   /api/projects/{projectId}/scenes/{sceneId}/keyframes/approval
DELETE /api/projects/{projectId}/scenes/{sceneId}/keyframes/approval
```

### Animated clips

```text
GET    /api/projects/{projectId}/scenes/{sceneId}/clips/
GET    /api/projects/{projectId}/scenes/{sceneId}/clips/settings
PUT    /api/projects/{projectId}/scenes/{sceneId}/clips/settings
POST   /api/projects/{projectId}/scenes/{sceneId}/clips/generate
GET    /api/projects/{projectId}/scenes/{sceneId}/clips/{variantId}/preview
POST   /api/projects/{projectId}/scenes/{sceneId}/clips/{variantId}/select
DELETE /api/projects/{projectId}/scenes/{sceneId}/clips/{variantId}
```

### Providers / jobs

```text
GET /api/providers/
GET /api/providers/{providerId}/settings
PUT /api/providers/{providerId}/settings

GET  /api/jobs/
GET  /api/jobs/{id}
POST /api/jobs/{id}/pause|resume|retry|restart|cancel
POST /api/jobs/projects/{projectId}/pause|resume|cancel
POST /api/jobs/projects/{projectId}/scenes/{sceneId}/pause|resume|cancel
GET  /api/jobs/events
```

## Persistence and media layout

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
    analysis/
    keyframes/
    generated/
    proxies/
    renders/
```

DuckDB stores structured metadata, jobs, dependencies, attempts, analysis/library metadata, and project/application settings. Large audio/image/video bytes remain filesystem media, not database blobs.

Visual Arc/storyboard/prompt history, keyframe variants/settings/approvals, and clip variants/settings use durable project settings where a separate schema table is not yet required. Generated media metadata remains in `media_assets`.

## Provider credentials

Provider settings store **credential references**, never resolved plaintext secret values. Environment-backed credentials are resolved at execution time; OS/external secret-store extension seams remain available.

Current normal development uses offline mocks and requires no paid-provider credentials. Real image/video adapters remain gated in `PLAN.md` until mock validation succeeds.

## Validation

Implementation completion belongs in `PLAN.md`. Executed proof belongs in `TESTPLAN.md`.

Core commands:

```bash
npm run lint
npm run typecheck
npm run test:frontend
npm run build:frontend

dotnet build backend/OpenMusicVideoCreator.sln -c Release
dotnet test backend/OpenMusicVideoCreator.sln -c Release
```

Or run the repository validation scripts:

```bash
./scripts/validate.sh
```

```powershell
./scripts/validate.ps1
```

**Do not infer success from source inspection.** Blocks 9/10 contain repository-side tests, but the new code is not considered validated until the current `TESTPLAN.md` matrix actually runs successfully.

## Repository structure

```text
frontend/
  app/
  src/api/
  src/features/
    projects/
    analysis/
    library/
    planning/
    generation/

backend/
  src/
    OpenMusicVideoCreator.Domain/
    OpenMusicVideoCreator.Application/
    OpenMusicVideoCreator.Infrastructure/
    OpenMusicVideoCreator.Api/
  tests/
    OpenMusicVideoCreator.ArchitectureTests/
    OpenMusicVideoCreator.Api.Tests/

docs/
  BLOCK9_KEYFRAME_GENERATION.md
  BLOCK10_VIDEO_GENERATION.md
```

## Development rules

Read before implementation work:

1. `AI_Music_Video_Studio_Master_Prompt.md`
2. `PLAN.md`
3. `TESTPLAN.md`
4. `AGENTS.md`
5. `ARCHITECTURE.md`
6. `SKILLS.md`

Keep modules focused, preserve provider independence, persist asynchronous state, keep user assets/generations non-destructive, and never claim a validation step passed unless it actually executed.
