# OpenMusicVideoCreator — Local Validation Test Plan

This file contains **validation that still needs to be executed locally**. `PLAN.md` tracks implementation; this file tracks proof that the implementation builds and behaves correctly.

Use this with a local Codex session that has the repository checked out, .NET/Node dependencies available, and FFmpeg/ffprobe installed. Do not mark an item complete unless the command/scenario actually ran successfully.

## How to execute

1. Start from an up-to-date clean checkout of `main`.
2. Record tool versions (`node`, `npm`, `dotnet`, `ffmpeg`, `ffprobe`).
3. Restore/install dependencies.
4. Run the baseline gates first.
5. Run block-specific scenarios in order.
6. Mark successful checks `[x]`.
7. Leave failed checks `[ ]` and add a short failure note under the item.
8. If a failure exposes missing implementation, add/reopen the corresponding item in `PLAN.md` and fix it before closing this test.

No paid-provider credentials are required for the baseline/mock test plan.

---

## Baseline repository gates

- [ ] Record `node --version`, `npm --version`, `dotnet --version`, `ffmpeg -version`, and `ffprobe -version`.
- [x] `npm install`
- [x] `dotnet restore backend/OpenMusicVideoCreator.sln`
- [ ] `npm run lint`
- [ ] `npm run typecheck`
  - 2026-08-10 earlier failure: `DirectorStoryboardPanel.tsx` and `SceneReferenceEditor.tsx` imported planning/storyboard client exports that were missing from `src/api/client`.
  - 2026-08-10 repository fix: the planning OpenAPI snapshot and typed client exports/operations were added. **Rerun still required; this item is intentionally not marked passed.**
  - Blocks 9–12 now add dedicated keyframe/clip/job/render/timeline client modules and workspaces; typecheck must cover those too.
- [ ] `npm run test:frontend`
- [ ] `npm run build:frontend`
- [x] `dotnet build backend/OpenMusicVideoCreator.sln -c Release`
  - This successful build predates the final Block 8–12 changes; rerun the baseline build before treating it as proof for current `main`.
- [ ] `dotnet test backend/OpenMusicVideoCreator.sln -c Release`
  - 2026-08-10 earlier run: 38/44 API tests passed. Failures: concurrent DuckDB initialization conflicts on `schema_migrations` (four tests), a planning scene-boundary expectation, and visual-library collection round-trip equality.
  - 2026-08-10 repository fix: the mock Director now uses a music-relative anchor tolerance that reaches the expected 58s structural boundary and tests also cover structured scene details. **Rerun still required; unrelated DuckDB/library failures remain unresolved until proven otherwise.**
  - Blocks 9–12 add keyframe/video/render/timeline/cancellation tests; none have been executed in the current connector-only environment.
- [ ] `./scripts/validate.sh` on Linux/macOS or `./scripts/validate.ps1` on Windows/PowerShell.
- [ ] `./scripts/run.ps1 -NoBrowser` starts the backend and frontend, serves `/healthz` and `http://localhost:3000`, and stops both processes with Ctrl+C.
- [x] `npm audit` reports no known vulnerabilities.

## Block 1 — Foundation

- [ ] Fresh clone can restore frontend/backend dependencies using README commands.
- [ ] Backend starts and `GET /healthz` returns success.
- [ ] `GET /api/system/version` returns the typed version response and a correlation ID.
- [ ] Frontend starts and can reach the local backend.
- [ ] Architecture dependency tests pass.

## Block 2 — Persistence, projects, media storage

- [ ] Temporary DuckDB integration tests pass on the local filesystem.
- [ ] Create/update/delete/reopen a project and confirm state survives backend restart.
- [ ] Portable project export/import round trip preserves project metadata.
- [ ] Media bytes are stored outside DuckDB and metadata/checksum are persisted.
- [ ] Path traversal test rejects unsafe media names/locations.
- [ ] Replacing/removing project references does not silently remove generated media metadata.

## Block 3 — Provider abstraction and mocks

- [ ] Provider catalog API returns capability-driven mock providers/models.
- [ ] Provider settings persist credential references but not resolved secret values.
- [ ] Unsupported provider/model capability combinations are rejected.
- [ ] Mock Director/Image/Video success, delay, rate-limit, quota, rejection, transient, and permanent scenarios pass.
- [ ] Inspect persisted DuckDB/provider API output to confirm no plaintext test secret appears.

## Block 4 — Persistent job engine

- [ ] Legal/illegal job state-transition tests pass.
- [ ] Dependencies and parent/child job metadata survive repository recreation.
- [ ] Two worker claims cannot execute the same queued job simultaneously.
- [ ] Pause/resume/cancel/retry/restart semantics pass at job/project/scene scope.
- [ ] Run `JobExecutionCancellationTests`; confirm active local execution receives the ephemeral cancellation signal while persisted job state remains authoritative.
- [ ] Cancel a currently executing local job through the single-job API and confirm its dispatcher token is cancelled and the worker does not later apply a stale success/failure result over persisted `Cancelled` state.
- [ ] Cancel project- and scene-scoped active jobs and confirm all matching local execution signals are cancelled without affecting unrelated jobs.
- [ ] `WaitingForQuota` survives backend/repository restart and resumes on the same graph.
- [ ] A persisted provider task ID is reconciled rather than blindly resubmitted.
- [ ] Completed work is not regenerated by normal resume.
- [ ] SSE reconnect returns/streams current persisted state rather than relying on lost in-memory events.

## Block 5 — Simple Mode and song attachment

- [ ] Frontend project list loads from the backend after page refresh.
- [ ] Create a project in Simple Mode and reopen it after browser/backend restart.
- [ ] Edit and delete a project from the UI.
- [ ] Upload a supported song and confirm the source file + DuckDB metadata/reference are created.
- [ ] Replace the song and confirm the prior asset is retained non-destructively.
- [ ] Reject empty/oversized/unsupported/path-like song uploads.
- [ ] Confirm Simple Mode exposes no provider IDs, model IDs, seeds, negative prompts, or raw model JSON.
- [ ] Keyboard-only pass: project selection, form fields, file selection, save/delete, visible focus.
- [ ] Offline/reconnect pass: disconnect backend/network, verify visible error/offline state, reconnect and retry.
- [ ] Responsive pass at desktop, tablet (~900px), and narrow mobile widths.

## Block 6 — Song analysis and Structure Map

- [ ] Generate or select a known audio fixture and run `ffprobe` metadata extraction through the application.
- [ ] Run FFmpeg waveform/energy analysis and verify it completes without loading the full decoded song into application memory.
- [ ] Confirm duration/sample rate/channels/codec/bitrate are persisted from ffprobe.
- [ ] Confirm waveform buckets, normalized energy points, beat candidates, BPM estimate, bar markers, phrase windows, quiet ranges, vocal/instrumental estimates, and section suggestions are present where applicable.
- [ ] Confirm uncertain/low-information audio can return unknown/null rhythm/vocal estimates without failing analysis.
- [ ] Re-run analysis and confirm a new immutable analysis version is created.
- [ ] Edit Structure Map section labels/types/boundaries and confirm a new version is created while the prior version remains available.
- [ ] Reject overlapping, negative, reversed, or out-of-duration section ranges.
- [ ] Restart backend and confirm latest analysis/Structure Map is restored.
- [ ] Verify waveform, beat/bar/phrase/quiet overlays and authoritative supplied-lyrics lane render in the UI.
- [ ] Optional transcription timing with normalized/mock transcription segments produces timing suggestions while leaving supplied lyric text unchanged.
- [ ] Reject/ignore transcription text differences unless the user explicitly edits the authoritative lyrics.
- [ ] Verify analysis/version changes expose dependency/provenance IDs without deleting unaffected downstream assets.

## Block 7 — Reusable Character, Style, Location, and Asset libraries

- [ ] Database migration upgrades a v4 database to schema v5 and creates `library_assets`, `visual_library_items`, and `project_character_states` without losing prior data.
- [ ] Create Character, Style, and Location entries through the API/UI and reopen them after backend restart.
- [ ] Character round trip preserves reference type, appearance, forbidden changes, outfits, and default continuity locks.
- [ ] Style round trip preserves prompt, camera, lighting, and animation characteristics.
- [ ] Location round trip preserves environment, constraints, lighting, weather, and time of day.
- [ ] Search by name/description/tag and favorites filtering return expected library items.
- [ ] Select the same Character/Style/Location in two different projects and confirm both projects store only stable library IDs, not copied library metadata.
- [ ] Attempt to delete a Character/Style/Location referenced by a project and confirm HTTP 409 includes referencing project IDs.
- [ ] Remove all project references, delete the library item, and confirm deletion succeeds without deleting unrelated media.
- [ ] Upload PNG/JPEG/WebP/GIF and a short supported video reference; confirm global media is stored below `library/originals/` and metadata uses `project_id = NULL`.
- [ ] FFmpeg creates a bounded PNG preview under `library/previews/` for supported image/video media.
- [ ] Preview endpoint returns the derived image and accepts range-safe normal browser requests.
- [ ] Test visual filename with spaces and shell metacharacters; confirm FFmpeg argument-list invocation treats it only as a path.
- [ ] Reject `../`, `..\\`, rooted/segmented, unsupported-extension, non-image/video, empty, and oversized visual uploads.
- [ ] Asset metadata supports tags, favorites, search, source description, and preview presence after restart.
- [ ] Attempt to delete an Asset Library entry referenced by a Character/Style/Location and confirm HTTP 409 includes referencing item IDs.
- [ ] Delete an unreferenced Asset Library entry and confirm only the index entry is removed; underlying original/preview media remains for explicit cleanup/recovery.
- [ ] Save project-specific character outfit, continuity locks, presence/confidence/isolation state values; restart backend and confirm state is restored.
- [ ] Reject character state for a character not referenced by the project, an outfit belonging to another character, and state values outside 0–1.
- [ ] Project editor shows reusable Character/Style/Location selectors and preserves the Song reference while modifying visual selections.
- [ ] Character continuity UI remains separate from global Character metadata and requires a saved project reference.
- [ ] Visual Library UI supports create/edit/delete, search/type filter, favorites, asset upload, previews, and conflict feedback.
- [ ] Keyboard/focus/accessibility pass for selectors, library editor, asset controls, continuity locks, and range inputs.
- [ ] Responsive pass for reference selectors, library layout, and asset cards at desktop/tablet/mobile widths.

## Block 8 — AI Director, Visual Arc, storyboard, and prompt history

- [ ] Run `StructuredMockDirector_CreatesMusicAwareTypicalSceneCountWithoutRigidEqualSlices` and confirm a 180s fixture produces roughly 20–35 scenes, non-uniform durations, full contiguous coverage, and a boundary at/near the 58s musical transition.
- [ ] Confirm every generated scene exposes song section/associated lyric, purpose, emotion, composition, camera, lighting, environment motion, symbolism, continuity requirements, and Character/Style/Location references where selected.
- [ ] Create a Director plan in the UI, edit Visual Arc summary/points/controls, save, reload, and confirm a new persistent Visual Arc version exists.
- [ ] Edit only one storyboard scene, including Character/Style/Location checkboxes and structured creative fields; confirm other scene content remains unchanged and a new storyboard version is created.
- [ ] Reorder two scenes and confirm their musical timing slots remain contiguous/non-overlapping while scene content moves to the new sequence positions.
- [ ] Confirm Director Intent remains separately visible from Final Provider Prompt and that prompt template/version metadata is shown in history.
- [ ] Regenerate a scene prompt with refinement notes and confirm no generation job/image/video call is created.
- [ ] Confirm the regenerated prompt uses the storyboard's referenced Visual Arc controls and exact `SongAnalysisId`, even if a newer song analysis is created after the storyboard.
- [ ] Confirm editing/saving a Visual Arc version preserves its original `SongAnalysisId` instead of silently rebasing to the newest analysis.
- [ ] Restart/recreate repositories and confirm Visual Arc, storyboard structured details, prompt versions, selected prompt ID, and prompt-to-storyboard provenance survive.
- [ ] Confirm downstream keyframe variants retain immutable `PromptVersionId` provenance so later generation can be attributed to the exact prompt revision.
- [ ] Run frontend Director source tests and TypeScript typecheck; confirm the planning API snapshot/client functions used by `DirectorStoryboardPanel` and `SceneReferenceEditor` compile.
- [ ] Keyboard/responsive pass for Director controls, Visual Arc cards, storyboard cards, scene inspector, reference checkboxes, prompt history, and action buttons.

## Block 9 — Keyframe generation and scene variants

- [ ] Run `KeyframeGenerationFlowTests`, `KeyframeVariantTests`, and keyframe approval tests; confirm all new Block 9 repository tests pass.
- [ ] Run `frontend/tests/keyframe-workspace-ui.test.mjs`, `npm run typecheck`, lint, and frontend build against the current Block 9 source.
- [ ] Generate a Start keyframe through `POST /api/projects/{projectId}/scenes/{sceneId}/keyframes/generate`; confirm HTTP returns before generation completes and the persisted job later reaches `Completed`.
- [ ] Enable optional End generation and confirm Start/End variants preserve their exact `PromptVersionId`, job ID, provider/model, generated media ID, cost fields, and variant history after backend restart.
- [ ] Confirm generated keyframe preview bytes are stored below the project keyframe area and served by the preview endpoint with range-safe browser behavior.
- [ ] Build a Character with outfit/base assets plus Style/Location references; confirm the request prioritizes outfit/base Character references, then Style/Location references, and never exceeds `MaxReferences`.
- [ ] Regenerate one Start keyframe while an older successful variant is selected; confirm the selected variant remains selected until the new completed variant is explicitly selected.
- [ ] Regenerate Scene N and verify selected assets/variants for all other scenes remain unchanged.
- [ ] Confirm a selected successful variant cannot be deleted and older unselected successful variants remain recoverable.
- [ ] Confirm keyframe approval requires a completed selected Start variant, optional selected End variant, and is invalidated/revoked when the current selection changes.
- [ ] Confirm Simple Mode exposes no provider/model/seed/negative-prompt controls or provider identifiers in variant cards.
- [ ] Confirm Advanced/Custom exposes only controls supported by the chosen model and rejects unsupported seed/negative-prompt/resolution values in the API.
- [ ] Simulate rate limit, quota exhaustion, provider outage, transient failure, rejection, and permanent failure; verify job states follow Block 4 retry/wait/terminal semantics without replacing successful variants.
- [ ] After retry exhaustion or a final terminal provider failure, verify the keyframe variant does not remain permanently shown as `Queued`; if it does, reopen Block 9 state-synchronization implementation.
- [ ] Restart during queued/generating keyframe work and confirm job/variant/prompt/media provenance recovers from persisted state rather than creating a blind duplicate generation.
- [ ] Keyboard/responsive pass for scene selection, generation controls, variant cards, compare/select/delete, approval, and Advanced/Custom settings.

Real image-provider validation is intentionally deferred until the complete mock matrix above succeeds.

## Block 10 — Image-to-video/video generation, queue UI, and resumability

- [ ] Run `VideoGenerationFlowTests` and `VideoFallbackTests`; confirm clip settings persistence, mock video materialization, non-destructive selection, and fallback policy tests pass.
- [ ] Run `frontend/tests/video-generation-ui.test.mjs`, `npm run typecheck`, lint, and frontend build against the current animation/queue source.
- [ ] Attempt scene animation without current keyframe approval and confirm the API/UI refuses generation without creating a paid/provider job.
- [ ] Queue animation after approving a Start keyframe and confirm the new `scene.video.generate` job depends on the approved Start job; when End-frame guidance is enabled, confirm the End job is also a dependency.
- [ ] Confirm the video coordinator resolves only ImageToVideo-capable models with start-frame support and chooses a supported duration/resolution/aspect ratio; unsupported End-frame configuration is rejected.
- [ ] Generate a mock clip and verify provider execution completes asynchronously, materializes a playable MP4 under project generated media, persists `MediaAssetMetadata`, and exposes it through the range-enabled clip preview endpoint.
- [ ] Confirm clip variants persist exact prompt/start/end keyframe/job/provider/model/duration/aspect/resolution/cost provenance and survive backend/repository restart.
- [ ] Select one successful clip, regenerate the same scene, and confirm the prior selected clip remains selected and recoverable until the new successful variant is explicitly selected.
- [ ] Regenerate one scene and confirm completed/selected clips for other scenes are untouched.
- [ ] Simulate `QuotaExhausted`/credits exhaustion during a video job; confirm job state reaches `WaitingForQuota`, survives backend restart, and resumes/retries on the same persisted graph later.
- [ ] Simulate rate limit, provider unavailable, authentication failure, rejection, invalid parameters, network failure, timeout, transient failure, and permanent failure; confirm each maps to the expected waiting/retry/rejected/permanent state without corrupting completed media.
- [ ] With two compatible fake providers, confirm quota/outage/auth/network/timeout/transient/unsupported-primary failures may use the configured fallback and the actual fallback provider/model is persisted on the completed clip.
- [ ] Confirm moderation rejection, invalid parameters, and permanent failures do not silently fall back to another provider.
- [ ] In Custom mode disable fallback, force the primary provider to fail with a normally fallback-eligible failure, and confirm no alternative provider is attempted.
- [ ] Configure a candidate fallback that cannot preserve End-frame, duration, aspect ratio, or resolution semantics and confirm it is excluded from the fallback list.
- [ ] Confirm provider task IDs returned by video providers are persisted by the job engine and startup reconciliation does not blindly resubmit known provider-side work.
- [ ] After retry exhaustion or a final terminal provider failure, verify the clip variant does not remain permanently shown as `Queued`; if it does, reopen Block 10 state-synchronization implementation.
- [ ] Open the Generation Queue and confirm initial state comes from persisted `GET /api/jobs/`, subsequent job updates arrive through `/api/jobs/events` SSE, and there is no per-scene job polling loop.
- [ ] Disconnect/reconnect the SSE connection and confirm the browser reconnects, reloads persisted state on `ready`, and does not depend on lost in-memory events.
- [ ] Verify queue rows show provider/model in Advanced/Custom, state, elapsed time, attempts/retries, estimated/actual cost, next-run time, and error code/message where applicable; Simple Mode hides provider/model detail.
- [ ] Exercise job pause/resume/retry/restart/cancel plus project pause/resume/cancel actions from the queue and confirm only eligible jobs transition.
- [ ] Exercise scene-scoped job actions through the existing API and confirm only jobs for that project+scene are affected.
- [ ] Browser-play the embedded mock MP4 preview and confirm controls/range requests work in the supported desktop browser.
- [ ] Keyboard/responsive pass for animation settings, clip cards/video controls, queue filters, queue actions, error states, and reconnect status.

Real video-provider validation is intentionally deferred until the complete mock matrix above succeeds.

## Block 11 — Deterministic assembly, preview render, and initial export

- [ ] Run `ProjectRenderFlowTests`, `JobExecutionCancellationTests`, and `AdvancedTimelineRenderArgumentsTests`; confirm render provenance, lifecycle, active cancellation, and FFmpeg argument-construction tests pass.
- [ ] Run `frontend/tests/project-render-workspace-ui.test.mjs`, `frontend/tests/advanced-timeline-ui.test.mjs`, TypeScript typecheck, lint, and frontend build.
- [ ] Queue Preview through `POST /api/projects/{projectId}/renders/`; confirm HTTP returns `202` before FFmpeg completes and the persistent `project.render` job later reaches `Completed`.
- [ ] Confirm the render manifest uses the latest compatible Advanced timeline version when its exact Storyboard/Song IDs match; confirm a stale timeline is ignored rather than silently applied to a newer storyboard/song.
- [ ] Confirm unchanged Preview and Final renders have the same `timelineSha256`, `TimelineVersionId`, `StoryboardVersionId`, `SongMediaAssetId`, clip provenance, source trims/rates/freezes, transitions, transforms/color, overlays, and effects.
- [ ] Verify every FFmpeg source path is a separate `ProcessStartInfo.ArgumentList` entry; repeat with clip/song/overlay filenames containing spaces and shell metacharacters and confirm no shell interpretation occurs.
- [ ] Inspect generated FFmpeg mapping and confirm the only output audio input is the project's original uploaded Song; generated clip and overlay audio must never be mapped.
- [ ] Exercise source trim, playback rate, freeze extension, scale/position, crop, opacity, brightness/contrast/saturation, fade, overlay, and effect filters against a known fixture and confirm FFmpeg accepts the generated filter graph.
- [ ] Generate Preview and confirm it uses the lower-resolution/faster profile while Final uses the configured final resolution/H.264 profile; both retain the same timeline hash.
- [ ] Exercise configured 16:9, 9:16, and 1:1 project resolutions and confirm output dimensions are even and reuse selected source variants rather than triggering regeneration.
- [ ] Cancel an actively running FFmpeg render through the render-specific endpoint; confirm the process is killed, job/render become `Cancelled`, no output/media metadata is retained, and stale worker success cannot overwrite cancellation.
- [ ] Cancel an active render through the generic job/project cancellation path; confirm `ProjectRenderService` reconciles render history to `Cancelled` and removes partial output.
- [ ] Retry a failed/cancelled render and confirm it restarts the same persisted job and exact immutable manifest/hash, adds a new render attempt, and does not overwrite earlier attempts/outputs.
- [ ] Force a transient FFmpeg/persistence failure with retries available; confirm the first attempt closes failed while render state returns to queued/retry state, then exhaustion becomes terminal `Failed`.
- [ ] Restart the backend after queueing a render and confirm persisted render/job/manifest/attempt history remains the source of truth and prior completed outputs remain downloadable.
- [ ] Complete output and let the worker run ffprobe; confirm duration matches manifest within bounded frame tolerance and a valid audio stream exists before `Completed` state/media metadata is published.
- [ ] Force ffprobe validation failure; confirm newly stored output bytes and any created media metadata are cleaned up and source/generated assets remain untouched.
- [ ] Complete multiple Preview/Final renders and confirm prior render versions, output media IDs, timeline hashes, timeline version IDs, command logs, and attempt histories remain recoverable.
- [ ] Download a completed output through `/api/projects/{projectId}/renders/{renderId}/output` and confirm the MP4 is range-safe/playable in the supported browser.
- [ ] Keyboard/responsive pass for Preview/Final actions, render history, cancel/retry, attempt history, status/error state, provenance details, command-log disclosure, and download links.

True neighboring crossfade composition and subtitle authoring/rendering remain open implementation scope and must not be marked validated as current fade-in behavior.

## Block 12 — Advanced timeline editor and Scene Inspector

- [ ] Run `TimelineEditorServiceTests` and `AdvancedTimelineRenderArgumentsTests`; confirm timeline versioning, protected Song provenance, split/reorder/replace/restore, overlays/effects, and render-argument tests pass.
- [ ] Run `frontend/tests/advanced-timeline-ui.test.mjs`, TypeScript typecheck, lint, and frontend build against the current Advanced/Custom workspace.
- [ ] Initialize Advanced timeline from the latest storyboard; confirm it pins the exact `StoryboardVersionId`, original `SongMediaAssetId`, selected completed clip variants/media, and `MusicTrackLocked=true`.
- [ ] Restart backend/recreate repository and confirm all timeline versions survive under persistent project settings with parent-version provenance intact.
- [ ] Verify Advanced/Custom displays persisted Block 6 waveform, quiet ranges, song sections, phrase boundaries, beat/bar markers, and lyric timing for the exact current analysis version; Simple Mode must not mount these Advanced panels.
- [ ] Edit one clip's source in/duration, slight playback rate, freeze extension, transition, scale/position, crop, opacity, and basic color; confirm a new timeline version is created and the prior version remains byte-for-byte recoverable in persisted data.
- [ ] Move/reorder clips and confirm timeline slots remain contiguous; split a clip and confirm two new segments reference the same original generated media without mutating it.
- [ ] Replace a timeline segment with another completed variant from the same scene; reject incomplete variants, variants from another scene, missing media, or cross-project media.
- [ ] Restore an older timeline version and confirm restoration creates a new latest version rather than changing/deleting historical versions.
- [ ] Attempt restoration after replacing the project's Song; confirm a timeline tied to the previous Song is rejected/not silently applied.
- [ ] Add/update/delete overlay and effect records; confirm every change creates a new timeline version and validates project ownership/timing/bounds.
- [ ] Confirm Scene Inspector exposes Story, Character, Environment, Camera, Generation, and Prompt sections and completed-variant selection without adding unsupported provider fields.
- [ ] Use prompt refinement in Advanced Inspector and confirm it creates only a new prompt/storyboard revision; inspect the job table/queue and verify no paid image/video job was automatically created.
- [ ] Change one trim and one Fade transition, render again, and confirm the render manifest pins the new `TimelineVersionId`/edit hash while the previous timeline version and previous render remain available.
- [ ] Confirm original Song bytes/checksum and project Song reference are unchanged after every timeline edit and after rendering; generated clip source assets are also read-only.
- [ ] Confirm Crossfade remains visibly an intent/configuration but is not represented as a proven true neighboring xfade until the corresponding implementation item is completed.
- [ ] Keyboard/responsive pass for version restore/reset, analysis lanes, clip selection, move/split, inspector controls, variant selection, prompt-only regeneration, and horizontal timeline scrolling.

## Cross-cutting security/data checks after Blocks 1–12

- [ ] Search source/config/exported project data for accidentally committed/resolved credentials.
- [ ] Upload filenames containing `../`, `..\\`, separators, and invalid characters are rejected.
- [ ] FFmpeg/ffprobe execution uses argument lists/typed process invocation; test filenames containing spaces/shell metacharacters for clip, overlay, song, and output paths.
- [ ] Project/song/analysis/library/planning/generation/timeline/render operations do not mutate or silently delete original uploaded media or completed generated variants.
- [ ] Restart application between key operations and verify DuckDB/project settings/jobs/media metadata/timeline/render history remain the source of truth.
- [ ] Confirm generated keyframe/clip/render preview/download routes only open media metadata belonging to the requested project and cannot traverse outside the configured media root.
- [ ] Confirm Advanced timeline overlay IDs cannot reference cross-project media and render source resolution remains constrained by `LocalMediaPathResolver`.

---

## Future block validation

Add concrete executable checks here whenever Blocks 13–14 are implemented. Keep implementation checkboxes in `PLAN.md`; keep unexecuted proof here.
