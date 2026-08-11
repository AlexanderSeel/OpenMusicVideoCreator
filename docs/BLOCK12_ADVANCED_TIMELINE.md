# Block 12 — Advanced timeline editor and Scene Inspector

The Advanced Editor is a **versioned edit layer**, not a destructive conventional NLE. It stores timeline decisions separately from generated media and carries the exact selected timeline version into deterministic project rendering.

## Immutable timeline versions

`ProjectTimelineVersion` pins:

- project ID
- exact `StoryboardVersionId`
- exact original `SongMediaAssetId`
- parent timeline version ID
- protected-music flag
- ordered clip edit records
- overlay lane records
- effect lane records
- timed subtitle records

Every persisted edit creates a new version. Restoring an older compatible version clones it into a new latest version rather than rewriting history.

The original Song is always `MusicTrackLocked = true` in Block 12 timeline versions. The timeline service exposes no operation that mutates/replaces that audio reference.

Timeline edits also resolve the current storyboard/song before mutation. If the storyboard changed, the service creates the current compatible timeline rather than modifying stale state. Restoring a timeline from a different Song or older storyboard version is rejected explicitly.

## Clip edits

`TimelineClip` retains generated provenance (`SceneId`, `ClipVariantId`, `MediaAssetId`) while storing reversible edit decisions:

- timeline position/duration
- source in/source duration
- slight playback-rate change
- freeze-frame extension
- Cut/Fade/Crossfade and duration
- scale and position
- source crop
- opacity
- brightness/contrast/saturation

Supported operations include:

- initialize/reset from the current storyboard and selected completed clips
- trim/settings update
- move/reorder
- split
- replace with another completed variant from the same scene
- restore an earlier compatible timeline version as a new version

Regenerating scene media remains in the existing generation workspace and still creates a new non-destructive variant. The timeline chooses among completed variants rather than mutating provider outputs.

## Music-reference lanes

The Advanced workspace reuses the existing persisted Block 6 Song Analysis contract rather than deriving another waveform model.

It displays:

- waveform buckets
- quiet ranges
- beat markers
- bar markers
- phrase boundaries
- song-structure sections
- transcription-assisted lyric timing when it belongs to the same analysis version

This keeps Advanced editing aligned with the same musical provenance used by Director planning.

## Scene Inspector

The inspector is grouped into the planned sections:

- **Story** — purpose, lyric, action
- **Character** — scene references, emotion, continuity requirements
- **Environment** — environment, lighting, environment motion
- **Camera** — camera/composition plus transform/crop/color controls
- **Generation** — completed variant selection and timeline source/playback/transition controls
- **Prompt** — prompt history and prompt-only regeneration

Prompt regeneration calls the existing Block 8 prompt-versioning operation and does **not** automatically queue image/video generation.

Provider/model-specific generation settings remain in the capability-aware Block 9/10 generation workspaces. The timeline does not invent generic fields for unsupported provider features.

## Composition lanes

The Advanced Editor includes editable Overlay, Effect, and Subtitle lanes. Each add/update/delete operation creates a new timeline version.

Overlay records support:

- project-owned media asset
- start/end time
- X/Y position
- scale
- opacity

Current bounded effect kinds are:

- Fade to black
- Vignette
- Grayscale

Subtitle records support:

- text
- start/end time
- vertical position
- relative size
- opacity

Subtitle text is validated and persisted with the timeline. The render engine burns it into Preview/Final output through escaped FFmpeg `drawtext` filters.

## Rendering integration

`ProjectRenderService` prefers the latest timeline version only when it matches the current storyboard and original Song IDs. A stale timeline is never silently applied to a newer storyboard/song.

The render manifest persists:

- `TimelineVersionId`
- source trim/rate/freeze settings
- transition metadata
- transforms/crop/color/opacity
- overlays/effects/subtitles

All content-affecting values participate in the deterministic timeline SHA-256 hash.

`FfmpegProjectRenderEngine` consumes those values while still mapping only the protected original Song as output audio. Generated clip/overlay audio is never mapped into the final mix.

Render composition supports source trim, rate/freeze, transform/crop, color/opacity, Fade, true neighboring Crossfade with `xfade`, overlays, bounded effects, and burned-in subtitles. Crossfade extends the outgoing clip by exactly the transition duration and begins at the incoming clip's nominal timeline boundary so the song/timeline duration stays unchanged.

## Validation status

Repository-side tests cover:

- immutable timeline versions
- protected Song provenance
- split/reorder/variant replacement/version restore
- stale-storyboard edit/restore protection
- overlay/effect/subtitle versioning
- Advanced FFmpeg argument construction and original-Song-only audio mapping
- true Crossfade graph construction
- subtitle escaping/timing
- frontend Advanced workspace/analysis/inspector/composition source contracts

These tests, typecheck, browser behavior, and actual FFmpeg filters have **not been claimed successful until they are executed locally**. See `TESTPLAN.md`.
