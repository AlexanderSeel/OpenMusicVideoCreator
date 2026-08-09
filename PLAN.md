# OpenMusicVideoCreator — Implementation Plan

Source of truth: `AI_Music_Video_Studio_Master_Prompt.md`.

This file contains **unfinished work only**. Work in complete blocks. A block is removed only after its implementation and repository-side acceptance criteria are genuinely complete and validated.

## Execution rules

- Implement whole blocks or coherent sub-blocks that leave the product in a working, reviewable state; do not accumulate disconnected TODO-sized slices.
- Preserve the master workflow: **Song → Analysis → Storyboard → Keyframes → Animated clips → Review/regenerate → Final video**.
- Every automated result must remain editable and non-destructive.
- Keep domain logic independent from AI vendors, HTTP, DuckDB, FFmpeg, and UI frameworks.
- Prefer modular, reusable components and focused functions/classes over duplicated logic or large god-components/services.
- Keep the MVP deployable as one backend process with clear logical service boundaries. Do not split into distributed microservices unless an actual scaling/deployment requirement justifies it.
- Remote AI generation is always asynchronous and persisted. In-memory queues may wake workers, but may never be the source of truth.
- Never overwrite a successful generated asset when regenerating. New attempts create new versions/variants.
- Never store provider API keys in DuckDB or project files.
- Never require paid provider calls in automated tests. Mock/fake providers must cover success and failure paths.
- Do not claim tests, builds, type checks, rendering checks, or provider integrations passed unless they actually ran successfully.
- Keep `README.md`, `ARCHITECTURE.md`, and this plan current as implementation progresses.

---

## Block 1 — Repository foundation and executable skeleton

Create the initial monorepo/application skeleton and architectural seams before product features.

### Scope

- Create a Next.js + React + TypeScript frontend workspace.
- Create an ASP.NET Core backend solution with clear layers:
  - Domain
  - Application
  - Infrastructure
  - API/Host
- Establish provider, persistence, media-storage, job, and rendering interfaces in the correct layers without prematurely implementing vendors.
- Establish a typed API contract strategy between frontend and backend.
- Add configuration conventions for development/test/production.
- Add structured logging and correlation IDs for project/job/provider operations.
- Add repository-level formatting, linting, type-checking, build, and test commands.
- Create `README.md` with local prerequisites and startup commands.
- Create `ARCHITECTURE.md` documenting boundaries, dependency direction, deployment shape, persistence, media storage, jobs, providers, and rendering.
- Add baseline CI that builds and tests frontend/backend without paid APIs.

### Acceptance

- Fresh clone can restore dependencies and start frontend/backend locally.
- Frontend can call a backend health/version endpoint through the typed API path.
- Backend dependency direction is enforced by project references/tests where practical.
- CI executes frontend lint/typecheck/tests/build and backend restore/build/tests.
- `README.md` and `ARCHITECTURE.md` describe the actual repository rather than intended future state.

---

## Block 2 — Persistence, project domain, and media storage

Implement the durable project foundation using DuckDB for metadata and files/object storage for media.

### Scope

- Define project aggregate/data model for title, artist, lyrics, storyline, meaning, visual direction, mood, genre, output targets, preset, budgets, and references.
- Implement DuckDB connection/migration/bootstrap strategy.
- Implement repositories for projects and application/project settings.
- Implement media storage abstraction and local filesystem implementation.
- Persist media metadata: URI/path, checksum, MIME type, dimensions, duration, size, and creation source.
- Create deterministic per-project directory layout matching the master prompt.
- Implement project CRUD API and tests.
- Implement portable `project.json` export/import representation without making it authoritative at runtime.
- Validate uploaded filenames/paths and prevent path traversal.

### Acceptance

- Project data survives backend restart.
- Project create/read/update/delete and settings persistence have automated integration tests against a real temporary DuckDB database.
- Media blobs are not stored inside DuckDB.
- Import/export round-trip preserves supported project metadata.
- Deleting or replacing references does not silently destroy unrelated generated assets.

---

## Block 3 — Provider abstraction, credentials, and mock providers

Create provider-independent capability contracts before integrating paid services.

### Scope

- Define capability interfaces for text, image, image editing, video, image-to-video, video-to-video, lip sync, upscale, transcription, and vision evaluation.
- Define provider/model capability metadata: reference support, start/end frame support, seed, negative prompts, durations, aspect ratios, resolutions, reference limits, native audio, etc.
- Define provider/model catalog abstraction with dynamic discovery where available and separately updateable static catalogs otherwise.
- Implement provider settings: enabled, credential reference, model defaults, concurrency, timeout, retries, allowed operations, priority, fallback priority.
- Implement credential abstraction using environment variables and OS/external secret references; DuckDB stores references only.
- Define normalized provider request/result/error contracts.
- Add `MockDirectorProvider`, `MockImageProvider`, and `MockVideoProvider` supporting success, delayed completion, rate limit, quota exhaustion, rejection, transient failure, and permanent failure.

### Acceptance

- Core/Application code has no direct dependency on a concrete AI vendor SDK.
- UI/API can query provider/model capabilities without hard-coded global model assumptions.
- Secrets are never returned by APIs or persisted in DuckDB/project exports.
- Mock providers can drive all normal automated tests offline.

---

## Block 4 — Persistent job engine and generation state machine

Implement recoverable asynchronous execution before real generation workflows.

### Scope

- Persist jobs, attempts, dependencies, provider task IDs, timestamps, retries, errors, costs, and scheduling metadata.
- Implement the explicit state machine from the master prompt, including waiting/retry/rejected/permanent states.
- Implement dependency handling and parent/child generation jobs.
- Implement background worker polling/claiming with safe concurrency.
- Implement pause/resume/cancel/retry/restart semantics for project and scene jobs.
- Implement retry scheduling with provider reset times/backoff where available.
- Implement startup recovery/reconciliation for jobs left in in-progress states.
- Implement live status stream through SSE initially; keep transport replaceable.
- Implement provider error classification and normalization.

### Acceptance

- Automated tests cover legal/illegal state transitions, dependencies, retry, pause/resume, cancellation, quota wait, provider wait, restart recovery, and duplicate-worker protection.
- Closing/restarting the backend does not lose queued/completed/failed job state.
- Completed work is not regenerated on resume.
- A `WaitingForQuota` job can later resume without recreating the project-generation graph.

---

## Block 5 — Simple Mode project creation and application shell

Create the first user-facing workflow on top of durable project data.

### Scope

- Desktop-first application shell with progressive disclosure: Simple, Advanced, Expert/Custom.
- Project list/dashboard and project creation wizard.
- Song upload, lyrics, storyline/meaning/direction, output target, preset, and budget inputs.
- Placeholder/selectors for Character, Style, and Location libraries that become functional in Block 7.
- Implement Fast, Balanced, Best Quality, Cheapest, and Custom preset domain models without tying them to specific vendors.
- Add accessible loading, error, empty, and offline/reconnect states.
- Establish reusable design tokens/components instead of page-specific styling duplication.

### Acceptance

- User can create, reopen, edit, and delete a project from the UI.
- Refresh/restart restores project state.
- Simple Mode does not expose provider IDs/seeds/model-specific JSON.
- Core UI components meet keyboard/focus/accessibility checks used by the project.

---

## Block 6 — Song ingestion, analysis, waveform, and editable Structure Map

Implement the music-driven foundation of every video.

### Scope

- Use ffprobe/FFmpeg for authoritative media metadata and deterministic media preparation where applicable.
- Implement song analysis pipeline for duration, BPM/beat candidates, bars, phrases, energy/dynamics, quiet sections, vocal/instrumental estimates, and section suggestions.
- Evaluate/reuse relevant concepts from the HyperFrames `music-to-video` skill without coupling application architecture to HyperFrames.
- Persist analysis and versions.
- Generate waveform data suitable for efficient frontend rendering.
- Implement editable Structure Map with song sections and boundaries.
- Implement beat/bar/section markers and lyrics lane.
- Add optional transcription-assisted lyric timing while preserving supplied lyrics as authoritative text.
- Changes to analysis/sections must version/invalidate only dependent downstream artifacts.

### Acceptance

- Uploaded audio produces a persisted analysis and visible waveform/Structure Map.
- User can adjust section boundaries and labels and reopen them after restart.
- Supplied lyrics remain unchanged by transcription assistance unless the user edits them.
- Tests cover analysis persistence, boundary validation, and downstream invalidation rules.

---

## Block 7 — Reusable Character, Style, Location, and Asset libraries

Implement reusable visual references independent of a single project.

### Scope

- Character Library including reference types, appearance details, forbidden changes, outfits, and continuity locks.
- Style Library with prompts, references, camera/lighting/animation characteristics.
- Location Library with references, constraints, environment, lighting, weather, time of day.
- Unified Asset Library with tags, search, favorites, previews, reuse, and source tracking.
- Project-to-library references without copying metadata unnecessarily.
- Character project state model capable of later timeline curves.
- Media reference validation and thumbnail/proxy generation.

### Acceptance

- Library items can be reused across projects.
- Deleting a library item referenced by projects is handled explicitly and safely.
- Character continuity settings persist per project.
- References appear consistently in project creation/editing and scene planning.

---

## Block 8 — AI Director, Visual Arc, storyboard, and prompt history

Implement the planning loop before spending on image/video generation.

### Scope

- Director input contract combining song analysis, lyrics, storyline, styles, characters, and locations.
- Director controls for literal↔symbolic, narrative strength, abstraction, emotion, darkness/warmth, surrealism/realism, complexity, acting, and camera energy.
- Generate and persist editable Visual Arc.
- Generate storyboard scene boundaries aligned preferentially to musical structure.
- Implement full scene model and visual storyboard cards.
- Implement scene editing/reordering within timing constraints.
- Implement Director Intent vs Final Provider Prompt.
- Implement prompt templates, prompt versioning, regeneration, and prompt-to-generation provenance.
- Use structured outputs validated against application schemas.

### Acceptance

- Mock Director generates an editable Visual Arc and approximately appropriate scene count for a typical 3-minute song without rigid equal slicing.
- User can change one scene without rebuilding the entire storyboard.
- Every prompt revision remains recoverable and is attributable to assets it generated.
- Storyboard and prompt history survive restart.

---

## Block 9 — Keyframe generation and scene variants

Implement the first paid-capability-ready generation stage using mocks first.

### Scope

- Route image/keyframe requests through provider capability interfaces.
- Generate start keyframes and optional end keyframes per scene.
- Automatically attach character/style/location references according to continuity settings and provider limits.
- Persist generation attempts, assets, prompts, cost estimates/actuals, and variants.
- Implement scene variant compare/select/delete/regenerate UI.
- Implement per-scene generation settings in Advanced/Custom mode.
- Add approval workflow before animation/video generation.
- Integrate at least one real image provider only after mock path is stable.

### Acceptance

- Full keyframe flow works offline with mocks and asynchronously through the job engine.
- Regenerating Scene N does not modify selected assets for other scenes.
- Selected variant is a reference; older successful variants remain intact.
- Real-provider integration is contract-tested/mocked and disabled when credentials are absent.

---

## Block 10 — Image-to-video/video generation, queue UI, and resumability

Implement scene animation with robust provider failure handling.

### Scope

- Video/image-to-video provider adapters and capability-aware request construction.
- Generation Coordinator creates scene job dependencies without blocking HTTP requests.
- Global Generation Queue UI with provider, model, state, elapsed time, retries, cost, errors, and actions.
- Scene-level pause/retry/restart/cancel and project-level pause/resume/retry-failed.
- Quota/rate-limit/provider-outage/auth/rejection/invalid-parameter/network/timeout handling.
- Optional automatic fallback according to preset/policy; Custom can disable fallback.
- Persist provider task IDs and reconcile provider-side jobs after restart.
- Integrate at least one real video provider after mock path passes.

### Acceptance

- Simulated credits exhaustion during a scene reaches `WaitingForQuota`, survives application restart, and resumes cleanly later.
- Dependent scenes wait safely and completed scenes remain untouched.
- User can regenerate one disliked scene and choose another variant without restarting the project.
- Queue updates arrive live without polling every scene from the browser.

---

## Block 11 — Deterministic assembly, preview render, and initial export

Complete the first genuinely useful end-to-end product loop.

### Scope

- FFmpeg/ffprobe wrapper with typed operations and safe argument handling.
- Assemble selected scene variants in timeline order.
- Preserve exact original uploaded song as final audio unless explicitly changed.
- Implement clip timing, trim, scale/crop, fades/cuts/basic transitions, overlays, and subtitles needed by MVP.
- Fast proxy/preview render and final H.264 MP4 1080p render.
- Support 16:9, 9:16, and 1:1 project outputs with reuse where possible.
- Persist render jobs, logs, outputs, and export history.
- Add render cancellation/retry and deterministic command/provenance logging.

### Acceptance

- Offline mock project can run Song → Analysis → Storyboard → Keyframes → Clips → Final MP4.
- Final duration and audio synchronization are validated with ffprobe.
- Preview render is lower-cost/faster than final render and uses the same timeline decisions.
- Render restart/failure does not corrupt source/generated assets.

---

## Block 12 — Advanced timeline editor and Scene Inspector

Add focused correction tools without turning the product into a general NLE.

### Scope

- Waveform/timeline with song structure, lyrics, beat/bar markers, clips, transitions, overlays, and effects lanes.
- Protected original music track by default.
- Non-destructive trim, move, split, replace, regenerate, extend, slight speed change, freeze frame, cut/crossfade/fade, transform, crop, opacity, and basic color controls.
- Scene Inspector sections for Story, Character, Environment, Camera, Generation, and Prompt.
- Provider-specific settings appear only when supported by selected model.
- Prompt regeneration does not automatically trigger paid generation.
- Editing operations create timeline/scene versions or reversible state rather than destructive file mutations.

### Acceptance

- User can change one transition and trim one scene, render again, and retain the prior render/timeline state.
- Advanced editor never modifies the original uploaded music file.
- Unsupported provider settings cannot be configured through the UI/API.

---

## Block 13 — QA, smart routing, cost controls, and continuity/state curves

Implement Pro functionality after the core generation loop is reliable.

### Scope

- Vision QA for identity, wardrobe/location continuity, prompt adherence, artifacts, unwanted characters, anatomy, and transition compatibility.
- Bounded auto-regeneration policies per preset; hard cap to prevent infinite spending loops.
- Model Router using capability, quality, cost, complexity, references, availability, quota/failure history, and estimated time.
- Project/scene/generation/provider/model cost accounting.
- Estimate-before-spend and hard project budget cap that pauses instead of silently exceeding budget.
- Generic character/environment state curves with interpolation into scene prompts.
- Director-generated curves plus manual curve editor.
- Multi-output reuse strategy so aspect/output variants do not regenerate unaffected assets unnecessarily.

### Acceptance

- Automated tests cover routing decisions, budget limits, QA retry caps, prompt/state interpolation, and cost aggregation.
- Cheapest preset does not auto-spend on avoidable QA regeneration.
- Best Quality can request multiple candidates but remains bounded by explicit retry/budget policy.
- State-curve changes update only affected downstream prompts/assets.

---

## Block 14 — Critical acceptance scenario and release hardening

Validate the exact scenario from the master prompt and close gaps discovered by real end-to-end use.

### Scope

- Automate as much of the 34-step critical acceptance scenario as practical with mock providers and fixture audio.
- Add browser-level happy-path coverage for project creation, storyboard editing, generation queue recovery, scene regeneration, Advanced Editor adjustment, and export.
- Add fault-injection tests for provider outage, quota exhaustion, backend restart, corrupted/missing media, FFmpeg failure, and interrupted downloads.
- Validate project portability/backups and recovery behavior.
- Review security boundaries for uploads, file paths, FFmpeg invocation, provider credentials, API responses, logs, and exported project metadata.
- Performance pass on large storyboard/timeline projects and generated asset libraries.
- Accessibility pass on primary Simple and Advanced workflows.
- Final documentation of supported providers, local requirements, limitations, and recovery procedures.

### Acceptance

- The master prompt's full critical acceptance scenario completes without architectural workarounds.
- CI/browser tests prove the offline/mock end-to-end flow.
- At least one configured real text/image/video provider path has been manually smoke-tested where credentials are available; absence of credentials does not fail normal CI.
- No known critical data-loss path remains for restart, retry, regeneration, or rendering.
