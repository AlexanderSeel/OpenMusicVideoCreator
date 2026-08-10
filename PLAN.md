# OpenMusicVideoCreator — Implementation Plan

Source of truth: `AI_Music_Video_Studio_Master_Prompt.md`.

This file is the visible **implementation tracker**. Completed repository-side implementation stays checked so progress remains auditable; unfinished implementation stays unchecked. Executable proof is tracked separately in `TESTPLAN.md` and may remain pending when the current environment cannot run the repository.

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
- Add repository-side automated test code with the implementation where behavior can reasonably be automated.
- Move build/lint/typecheck/unit/integration/browser/FFmpeg/manual checks that have not actually run to `TESTPLAN.md`; do not block implementation solely because execution is unavailable.
- Do not claim tests, builds, type checks, rendering checks, or provider integrations passed unless they actually ran successfully.
- Keep `README.md`, `ARCHITECTURE.md`, `PLAN.md`, and `TESTPLAN.md` current as implementation progresses.

---

## Block 1 — Repository foundation and executable skeleton

- [x] Next.js / React / TypeScript frontend skeleton.
- [x] ASP.NET Core backend solution with Domain, Application, Infrastructure, and API projects.
- [x] Typed frontend/backend API-contract strategy.
- [x] Configuration, structured logging, and correlation IDs.
- [x] Formatting/lint/typecheck/build/test commands.
- [x] `README.md` and `ARCHITECTURE.md` foundation documentation.
- [x] Repository validation/CI definition present.
- [x] Layer dependency tests enforce inward dependency direction.

## Block 2 — Persistence, project domain, and media storage

- [x] Durable project domain model and project CRUD service.
- [x] DuckDB schema bootstrap/migration infrastructure.
- [x] Project, application-settings, project-settings, and media-metadata repositories.
- [x] Local media-storage abstraction with deterministic project layout.
- [x] Media metadata separated from binary media bytes.
- [x] SHA-256 metadata and safe path handling.
- [x] Portable versioned project JSON export/import.
- [x] Non-destructive project-reference behavior.
- [x] Persistence/media integration tests.

## Block 3 — Provider abstraction, credentials, and mock providers

- [x] Capability interfaces for text/image/image-edit/video/image-to-video/video-to-video/lip-sync/upscale/transcription/vision/Director.
- [x] Provider/model capability descriptors and catalog abstraction.
- [x] Provider settings persisted independently from provider implementations.
- [x] Credential references instead of plaintext secret persistence.
- [x] Environment credential resolver plus OS/external secret-store extension seams.
- [x] Normalized provider result/failure model.
- [x] Offline Mock Director, Image, and Video providers.
- [x] Configurable mock success/delay/rate-limit/quota/rejection/transient/permanent scenarios.
- [x] Provider catalog/settings HTTP API and typed frontend contract.
- [x] Provider capability, secret non-leakage, validation, and failure-mode tests.

## Block 4 — Persistent job engine and generation state machine

- [x] Persist jobs, attempts, dependencies, provider task IDs, scheduling, retries, errors, costs, and claim metadata.
- [x] Explicit state machine from Draft through terminal/waiting/retry states.
- [x] Dependency handling and parent/child job metadata.
- [x] Background worker polling/claiming with one-process duplicate-claim protection.
- [x] Job/project/scene pause, resume, retry, restart, and cancel semantics.
- [x] Bounded retry scheduling with provider retry delay support.
- [x] Startup recovery for interrupted local work.
- [x] Provider-task reconciliation state avoids blind duplicate paid submissions.
- [x] SSE job-change stream with persisted state as source of truth.
- [x] Provider error classification mapped to operational job states.
- [x] Tests for state transitions, dependencies, retries, quota wait, provider wait, restart recovery, pause/resume/cancel, and duplicate claims.
- [x] `WaitingForQuota` survives repository recreation and resumes on the same job graph.
- [x] Completed work is not regenerated by normal resume.

---

## Block 5 — Simple Mode project creation and application shell

- [x] Desktop-first application shell with progressive disclosure: Simple, Advanced, Expert/Custom.
- [x] Project list/dashboard and project creation/edit workflow.
- [x] Song upload, lyrics, storyline/meaning/direction, output target, preset, and budget inputs.
- [x] Durable project `Song` media reference with non-destructive replacement semantics.
- [x] Character, Style, and Location selectors integrated with the reusable Block 7 Library.
- [x] Fast, Balanced, Best Quality, Cheapest, and Custom preset domain enum without provider coupling.
- [x] Accessible loading, error, empty, and offline/reconnect states.
- [x] Reusable design tokens/components instead of page-specific styling duplication.
- [x] Simple Mode UI refactored into orchestration, sidebar, form, and project-model helpers.
- [x] Typed project CRUD/song client contracts.
- [x] Repository tests added for song attachment and Simple Mode structure.

### Acceptance implementation

- [x] User-facing code supports create, reopen, edit, and delete from the UI.
- [x] Initial load/reload restores saved project state from the backend.
- [x] Simple Mode does not expose provider IDs, seeds, or model-specific JSON.
- [x] Core UI includes keyboard focus, semantic labels/tab roles, live/error states, reduced-motion handling, and responsive fallback.

Execution proof is tracked in `TESTPLAN.md`.

---

## Block 6 — Song ingestion, analysis, waveform, and editable Structure Map

- [x] Use ffprobe/FFmpeg for authoritative media metadata and deterministic local media analysis where applicable.
- [x] Implement song analysis pipeline for duration, BPM/beat candidates, bars, phrases, energy/dynamics, quiet sections, heuristic vocal/instrumental estimates, and section suggestions.
- [x] Evaluate/reuse relevant music-to-video concepts without coupling runtime architecture to HyperFrames.
- [x] Persist immutable song analysis versions in DuckDB.
- [x] Generate bounded waveform data suitable for efficient frontend rendering without retaining full decoded audio in application memory.
- [x] Implement editable Structure Map with song sections and boundaries.
- [x] Implement beat/bar/phrase/quiet markers and authoritative lyrics lane.
- [x] Add provider-neutral transcription-assisted lyric timing while preserving supplied lyrics as authoritative text.
- [x] Persist independent lyric-timing versions linked to the source asset and exact song-analysis ID.
- [x] Changes to analysis/sections create new provenance IDs/versions so downstream artifacts can depend on exact analysis versions without destructive invalidation.
- [x] Repository-side tests cover analysis/versioning/rhythm inference/section boundaries and lyric timing preservation/versioning.

### Acceptance implementation

- [x] Uploaded audio has an implemented path to persisted local analysis and visible waveform/Structure Map.
- [x] User can edit section boundaries/labels; saves create persistent analysis versions that can be reopened.
- [x] Supplied lyrics remain unchanged by transcription assistance; only timing/confidence metadata is attached.
- [x] Low-information signal/transcription paths may return uncertain/null/unmatched estimates rather than fabricating certainty.

Execution proof is tracked in `TESTPLAN.md`.

---

## Block 7 — Reusable Character, Style, Location, and Asset libraries

- [x] Character Library with reference types, appearance details, forbidden changes, outfits, reference assets, and default continuity locks.
- [x] Style Library with prompts, references, camera/lighting/animation characteristics.
- [x] Location Library with references, constraints, environment, lighting, weather, and time of day.
- [x] Unified Asset Library with tags, search, favorites, previews, reuse, and source tracking.
- [x] Project-to-library references use stable IDs without unnecessary metadata duplication.
- [x] Project-specific Character outfit/continuity/state model supports normalized values that can become later timeline curves.
- [x] Media reference validation and FFmpeg thumbnail/preview generation use safe global library storage.
- [x] DuckDB schema v5 persists library assets/items and project character state.
- [x] Library CRUD/search/favorites/preview/delete-conflict APIs and typed frontend contracts.
- [x] Simple Mode reusable selectors, Library workspace, and project Character continuity UI.
- [x] Repository-side tests cover cross-project reuse, safe deletion, persistence, asset conflicts, and global path safety.

### Acceptance implementation

- [x] The same Character/Style/Location item can be referenced by multiple projects without copying its metadata.
- [x] Deleting a referenced visual-library or asset-library item is handled explicitly and safely with referencing IDs returned to the caller.
- [x] Character outfit/continuity/state settings are persisted separately per project.
- [x] Project creation/editing uses the same stable reference contract that Block 8 scene planning consumes, avoiding a second scene-specific library model.

Execution proof is tracked in `TESTPLAN.md`.

---

## Block 8 — AI Director, Visual Arc, storyboard, and prompt history

- [x] Director input contract combines song analysis, lyrics, storyline, styles, characters, and locations.
- [x] Director controls for literal↔symbolic, narrative strength, abstraction, emotion, darkness/warmth, surrealism/realism, complexity, acting, and camera energy.
- [x] Generate and persist editable Visual Arc.
- [x] Generate storyboard scene boundaries aligned preferentially to musical structure.
- [x] Implement full scene model and visual storyboard cards, including song section/lyric, purpose, emotion, composition, lighting, environment motion, symbolism, continuity, and reusable visual references.
- [x] Implement scene editing/reordering within timing constraints.
- [x] Implement Director Intent vs Final Provider Prompt.
- [x] Implement prompt templates, prompt versioning, regeneration, and prompt-to-generation provenance.
- [x] Validate structured AI outputs against application schemas.

### Acceptance implementation

- [x] Mock Director generates an editable Visual Arc and approximately appropriate scene count for a typical 3-minute song without rigid equal slicing.
- [x] User can change one scene without rebuilding the entire storyboard; that edit creates a new storyboard/prompt version.
- [x] Every prompt revision remains recoverable and attributable through immutable prompt IDs used by downstream keyframe variants.
- [x] Storyboard, structured scene details, and prompt history use durable project settings and survive repository recreation/restart.
- [x] Scene edit/reorder/prompt regeneration preserve the storyboard's exact song-analysis and Visual-Arc provenance rather than silently rebasing to newer analysis.

Execution proof is tracked in `TESTPLAN.md`.

---

## Block 9 — Keyframe generation and scene variants

- [ ] Route image/keyframe requests through provider capability interfaces.
- [ ] Generate start keyframes and optional end keyframes per scene.
- [ ] Attach character/style/location references according to continuity settings and provider limits.
- [ ] Persist generation attempts, assets, prompts, cost estimates/actuals, and variants.
- [ ] Implement scene variant compare/select/delete/regenerate UI.
- [ ] Implement per-scene generation settings in Advanced/Custom mode.
- [ ] Add approval workflow before animation/video generation.
- [ ] Integrate at least one real image provider after the mock path is stable.

### Acceptance

- [ ] Full keyframe flow works offline with mocks and asynchronously through the job engine.
- [ ] Regenerating Scene N does not modify selected assets for other scenes.
- [ ] Selected variant is a reference; older successful variants remain intact.
- [ ] Real-provider integration is contract-tested/mocked and disabled when credentials are absent.

---

## Block 10 — Image-to-video/video generation, queue UI, and resumability

- [ ] Video/image-to-video provider adapters and capability-aware request construction.
- [ ] Generation Coordinator creates scene job dependencies without blocking HTTP requests.
- [ ] Global Generation Queue UI with provider, model, state, elapsed time, retries, cost, errors, and actions.
- [ ] Scene-level pause/retry/restart/cancel and project-level pause/resume/retry-failed.
- [ ] Quota/rate-limit/provider-outage/auth/rejection/invalid-parameter/network/timeout handling in generation flows.
- [ ] Optional automatic fallback according to preset/policy; Custom can disable fallback.
- [ ] Persist provider task IDs and reconcile provider-side jobs after restart.
- [ ] Integrate at least one real video provider after the mock path passes.

### Acceptance

- [ ] Simulated credits exhaustion during a scene reaches `WaitingForQuota`, survives restart, and resumes cleanly later.
- [ ] Dependent scenes wait safely and completed scenes remain untouched.
- [ ] User can regenerate one disliked scene and choose another variant without restarting the project.
- [ ] Queue updates arrive live without polling every scene from the browser.

---

## Block 11 — Deterministic assembly, preview render, and initial export

- [ ] FFmpeg/ffprobe wrapper with typed operations and safe argument handling.
- [ ] Assemble selected scene variants in timeline order.
- [ ] Preserve exact original uploaded song as final audio unless explicitly changed.
- [ ] Implement clip timing, trim, scale/crop, fades/cuts/basic transitions, overlays, and subtitles needed by MVP.
- [ ] Fast proxy/preview render and final H.264 MP4 1080p render.
- [ ] Support 16:9, 9:16, and 1:1 outputs with reuse where possible.
- [ ] Persist render jobs, logs, outputs, and export history.
- [ ] Add render cancellation/retry and deterministic command/provenance logging.

### Acceptance

- [ ] Offline mock project can run Song → Analysis → Storyboard → Keyframes → Clips → Final MP4.
- [ ] Final duration and audio synchronization are validated with ffprobe.
- [ ] Preview render is lower-cost/faster than final render and uses the same timeline decisions.
- [ ] Render restart/failure does not corrupt source/generated assets.

---

## Block 12 — Advanced timeline editor and Scene Inspector

- [ ] Waveform/timeline with song structure, lyrics, beat/bar markers, clips, transitions, overlays, and effects lanes.
- [ ] Protected original music track by default.
- [ ] Non-destructive trim, move, split, replace, regenerate, extend, slight speed change, freeze frame, cut/crossfade/fade, transform, crop, opacity, and basic color controls.
- [ ] Scene Inspector sections for Story, Character, Environment, Camera, Generation, and Prompt.
- [ ] Provider-specific settings appear only when supported by the selected model.
- [ ] Prompt regeneration does not automatically trigger paid generation.
- [ ] Editing operations create timeline/scene versions or reversible state rather than destructive file mutations.

### Acceptance

- [ ] User can change one transition and trim one scene, render again, and retain the prior render/timeline state.
- [ ] Advanced editor never modifies the original uploaded music file.
- [ ] Unsupported provider settings cannot be configured through the UI/API.

---

## Block 13 — QA, smart routing, cost controls, and continuity/state curves

- [ ] Vision QA for identity, wardrobe/location continuity, prompt adherence, artifacts, unwanted characters, anatomy, and transition compatibility.
- [ ] Bounded auto-regeneration policies per preset with hard retry caps.
- [ ] Model Router using capability, quality, cost, complexity, references, availability, quota/failure history, and estimated time.
- [ ] Project/scene/generation/provider/model cost accounting.
- [ ] Estimate-before-spend and hard project budget cap.
- [ ] Generic character/environment state curves with prompt interpolation.
- [ ] Director-generated curves plus manual curve editor.
- [ ] Multi-output reuse strategy avoiding unnecessary regeneration.

### Acceptance

- [ ] Automated tests cover routing decisions, budget limits, QA retry caps, prompt/state interpolation, and cost aggregation.
- [ ] Cheapest preset does not auto-spend on avoidable QA regeneration.
- [ ] Best Quality can request multiple candidates but remains bounded by explicit retry/budget policy.
- [ ] State-curve changes update only affected downstream prompts/assets.

---

## Block 14 — Critical acceptance scenario and release hardening

- [ ] Automate as much of the master critical acceptance scenario as practical with mock providers and fixture audio.
- [ ] Add browser-level happy-path coverage for project creation, storyboard editing, generation queue recovery, scene regeneration, Advanced Editor adjustment, and export.
- [ ] Add fault-injection tests for provider outage, quota exhaustion, backend restart, corrupted/missing media, FFmpeg failure, and interrupted downloads.
- [ ] Validate project portability/backups and recovery behavior.
- [ ] Review security boundaries for uploads, file paths, FFmpeg invocation, provider credentials, API responses, logs, and exported metadata.
- [ ] Performance pass on large storyboard/timeline projects and asset libraries.
- [ ] Accessibility pass on primary Simple and Advanced workflows.
- [ ] Final documentation of supported providers, local requirements, limitations, and recovery procedures.

### Acceptance

- [ ] The master prompt's full critical acceptance scenario completes without architectural workarounds.
- [ ] Offline/mock end-to-end flow is proven by repository-side automated tests.
- [ ] At least one configured real text/image/video provider path is manually smoke-tested where credentials are available; absence of credentials does not fail normal tests.
- [ ] No known critical data-loss path remains for restart, retry, regeneration, or rendering.
