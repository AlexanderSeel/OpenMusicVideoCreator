# OpenMusicVideoCreator

OpenMusicVideoCreator is an AI-assisted music-video studio built around an editable, resumable workflow:

**Song → Analysis → Storyboard → Keyframes → Animated clips → Review/regenerate → Advanced edit → Final video**

The product specification is `AI_Music_Video_Studio_Master_Prompt.md`.

- `PLAN.md` tracks **implemented vs unfinished product work**.
- `TESTPLAN.md` tracks **validation that still needs to be executed locally**.
- `AGENTS.md` defines the repository development workflow.

## Current implementation

Repository-side implementation now covers Blocks 1–12, plus the offline/mock generation paths of Blocks 9 and 10. Real image/video provider integrations remain deliberately open until the complete mock validation matrix in `TESTPLAN.md` has actually passed.

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
- persistent asynchronous job engine with dependencies, retries, waiting states, provider task IDs, startup recovery, SSE updates, and active local cancellation signaling
- versioned AI Director, Visual Arc, storyboard, scene editing, and prompt history
- Start/End keyframe generation with immutable prompt provenance
- continuity-aware Character/Style/Location reference routing with provider reference limits
- non-destructive keyframe variants, compare/select/delete/regenerate, per-scene settings, and approval before animation
- scene image-to-video generation from approved keyframes
- non-destructive animated clip variants with prompt/keyframe/job/provider/model/cost provenance
- capability-aware duration/aspect/resolution/end-frame validation and compatible-provider fallback
- global Generation Queue UI driven by persisted jobs plus SSE notifications
- deterministic Preview/Final MP4 render jobs using the protected original Song as output audio
- render manifests with storyboard/selected-clip/timeline provenance and SHA-256 decision hashes
- ffprobe validation before render success, versioned render/attempt history, cancel/retry, partial-output cleanup, and downloads
- versioned Advanced timeline with protected Song, trim/move/split/replace, playback-rate/freeze, transforms/crop/color/opacity, Cut/Fade/true Crossfade, overlays/effects/subtitles, and reversible version restore
- Advanced music-reference lanes reusing persisted waveform/sections/phrases/beats/bars/quiet ranges/lyric timing
- editable Overlay, Effect, and Subtitle composition lanes; subtitles are burned into deterministic renders
- Scene Inspector sections for Story, Character, Environment, Camera, Generation, and Prompt
- Prompt-only regeneration without automatically starting paid generation

See:

- `docs/BLOCK9_KEYFRAME_GENERATION.md`
- `docs/BLOCK10_VIDEO_GENERATION.md`
- `docs/BLOCK11_RENDERING.md`
- `docs/BLOCK12_ADVANCED_TIMELINE.md`

Still open in the current PLAN are real image/video providers, QA/vision evaluation, smart model routing, budget/cost controls, continuity/state curves, multi-output reuse strategy, and release hardening.

## Prerequisites

- Node.js 22+
- npm 10+
- .NET 10 SDK
- Git
- **FFmpeg + ffprobe** on `PATH`

FFmpeg/ffprobe are runtime requirements for song analysis, visual-reference previews, deterministic project rendering, and render validation. Burned-in subtitles require an FFmpeg build with the `drawtext` filter available.

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

Simple Mode keeps provider internals and the Advanced timeline out of the primary workflow. It provides project/song/library/planning/generation/render controls while automatically routing supported capabilities.

Provider IDs, model IDs, seeds, negative prompts, raw provider JSON, provider-specific animation controls, and Advanced timeline inspector controls are hidden from Simple Mode.

### Advanced / Expert Custom

Advanced/Custom progressively expose capability-supported generation settings plus the versioned timeline editor.

Current generation controls include provider/model selection, supported resolution/duration, optional End-frame use, seed/negative prompt where supported, and Custom fallback policy.

The Advanced timeline exposes only provider-independent edit decisions and existing completed variants; unsupported provider/model fields are not invented by the timeline editor. Backend generation APIs continue to reject unsupported settings.

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

Supplied lyrics remain authoritative. Optional transcription segments only contribute timing/confidence metadata and never silently rewrite project lyrics.

Advanced timeline music-reference lanes reuse this same persisted analysis contract; they do not create a parallel waveform or rhythm model.

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

## Keyframe and scene video generation

Each scene can create a Start keyframe and optional End keyframe through the persistent job engine. Regeneration appends variants; selection is only a reference, so older successful variants remain intact.

Animation requires approval of the current completed Start selection and optional End selection. `scene.video.generate` jobs route `ImageToVideo` capabilities, validate supported duration/aspect/resolution/end-frame semantics, and persist non-destructive `SceneClipVariant` provenance.

### Fallback

When enabled, fallback candidates are included only if they can preserve the exact Start/End-frame, duration, aspect-ratio, and resolution semantics resolved for the primary request.

Fallback is permitted for operational/provider failures such as quota/credits, rate limit, outage, authentication, unsupported adapter capability, network, timeout, and transient failure. Moderation rejection, invalid parameters, and permanent failures do not silently switch providers.

Custom mode can disable fallback.

## Persistent Generation Queue

The frontend Generation Queue first reads persisted jobs from `GET /api/jobs/` and then consumes `/api/jobs/events` via `EventSource`.

SSE is only a notification mechanism; DuckDB jobs remain authoritative. Reconnect reloads persisted state.

Queue actions include job pause/resume/retry/restart/cancel plus project/scene scoped controls. Cancellation also signals already-running local work such as FFmpeg so a stale local execution cannot later overwrite persisted `Cancelled` state.

## Advanced timeline

Advanced/Custom mode can initialize a `ProjectTimelineVersion` from the current storyboard and selected completed clip variants.

Every version pins:

- exact `StoryboardVersionId`
- exact original `SongMediaAssetId`
- parent timeline version
- protected music flag
- ordered scene/variant/media provenance
- trim/playback/freeze settings
- transition kind/duration
- transform/crop/opacity/color settings
- overlay, effect, and subtitle lanes

Edits create new versions. Restoring an older compatible version creates another new version; generated media and original Song bytes are never rewritten. If the current storyboard changes, timeline edits rebase to a current compatible timeline and stale clip IDs are rejected rather than silently modifying old state.

The Scene Inspector contains Story, Character, Environment, Camera, Generation, and Prompt sections. Prompt refinement uses the existing prompt-versioning operation and does not auto-start generation.

Composition controls can add/update/delete:

- project-owned image/video overlays with timing, position, scale, and opacity
- bounded Fade-to-black, Grayscale, and Vignette effects
- timed subtitles with text, vertical position, size, and opacity

## Deterministic rendering

Preview and Final export run as persistent `project.render` jobs.

`ProjectRenderService` prefers the latest Advanced timeline only when its exact Storyboard and Song IDs still match the current project state. Otherwise it falls back to the current storyboard/selected clips instead of silently applying stale edits.

The immutable render manifest pins source clip/media IDs, edit parameters, overlays/effects/subtitles, original Song, optional timeline version, output profile, and a deterministic timeline SHA-256 hash.

`FfmpegProjectRenderEngine` uses `ProcessStartInfo.ArgumentList` and safe media-path resolution. Current render filters consume:

- source trim
- playback-rate change
- freeze extension
- crop/scale/position
- brightness/contrast/saturation
- opacity
- Fade
- true neighboring Crossfade using `xfade`
- overlays
- Fade-to-black / Grayscale / Vignette effects
- burned-in timed subtitles using escaped `drawtext`

For Crossfade, the outgoing clip is extended by exactly the transition duration and the `xfade` starts at the incoming clip's nominal timeline boundary, preserving total song/timeline duration.

Generated clip/overlay audio is never mapped. The project's protected original Song is the only output audio source.

After FFmpeg output is stored, ffprobe validates duration and audio-stream presence before the render is marked successful. Failed/cancelled/invalid outputs are cleaned up; source/generated inputs remain read-only.

Render history is versioned and records attempts, deterministic command logs, output media, state/errors, and provenance. Cancel/retry retains the same immutable manifest for a given render version.

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

### Keyframes / animated clips

```text
/api/projects/{projectId}/scenes/{sceneId}/keyframes/...
/api/projects/{projectId}/scenes/{sceneId}/clips/...
```

### Advanced timeline

```text
GET    /api/projects/{projectId}/timeline/
GET    /api/projects/{projectId}/timeline/versions
POST   /api/projects/{projectId}/timeline/initialize
POST   /api/projects/{projectId}/timeline/reset
PUT    /api/projects/{projectId}/timeline/clips/{clipId}
POST   /api/projects/{projectId}/timeline/clips/reorder
POST   /api/projects/{projectId}/timeline/clips/{clipId}/replace
POST   /api/projects/{projectId}/timeline/clips/{clipId}/split
PUT    /api/projects/{projectId}/timeline/overlays
DELETE /api/projects/{projectId}/timeline/overlays/{overlayId}
PUT    /api/projects/{projectId}/timeline/effects
DELETE /api/projects/{projectId}/timeline/effects/{effectId}
PUT    /api/projects/{projectId}/timeline/subtitles
DELETE /api/projects/{projectId}/timeline/subtitles/{subtitleId}
POST   /api/projects/{projectId}/timeline/restore/{versionId}
```

### Project renders

```text
GET  /api/projects/{projectId}/renders/
POST /api/projects/{projectId}/renders/
GET  /api/projects/{projectId}/renders/{renderId}
POST /api/projects/{projectId}/renders/{renderId}/cancel
POST /api/projects/{projectId}/renders/{renderId}/retry
GET  /api/projects/{projectId}/renders/{renderId}/output
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

Versioned project-settings repositories currently persist Visual Arc/storyboard/prompt history, keyframe settings/variants/approval, clip settings/variants, render history, and Advanced timeline versions. Generated/rendered media metadata remains in `media_assets`.

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

**Do not infer success from source inspection.** The latest Blocks 9–12 repository changes are not considered validated until the current `TESTPLAN.md` matrix actually runs successfully.

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
    timeline/
    rendering/

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
  BLOCK11_RENDERING.md
  BLOCK12_ADVANCED_TIMELINE.md
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
