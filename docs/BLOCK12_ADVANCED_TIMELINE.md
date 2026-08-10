# Block 12 — Advanced timeline editor and Scene Inspector

The Advanced Editor is a **versioned edit layer**, not a destructive video editor. It stores timeline decisions separately from generated media and carries the selected timeline version into deterministic project rendering.

## Immutable timeline versions

`ProjectTimelineVersion` pins:

- the project ID
- exact `StoryboardVersionId`
- exact original `SongMediaAssetId`
- parent timeline version ID
- protected-music flag
- ordered clip edit records
- overlay lane records
- effect lane records

Every persisted edit creates a new version. Restoring an older version clones it into a new latest version rather than rewriting history.

The original Song is always `MusicTrackLocked = true` in Block 12 timeline versions. The timeline service does not expose an operation that mutates/replaces that audio reference.

## Clip edits

`TimelineClip` retains generated provenance (`SceneId`, `ClipVariantId`, `MediaAssetId`) while storing reversible edit decisions:

- timeline position/duration
- source in/source duration
- slight playback-rate change
- freeze-frame extension
- Cut/Fade/Crossfade intent and duration
- scale and position
- source crop
- opacity
- brightness/contrast/saturation

Supported application operations currently include:

- initialize/reset from the current storyboard and selected completed clips
- trim/settings update
- move/reorder
- split
- replace with another completed variant from the same scene
- restore an earlier timeline version as a new version

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

## Overlay and effect lanes

Timeline domain/API/rendering support exists for versioned overlay and effect lanes.

Overlay records include:

- project media asset
- start/end time
- X/Y position
- scale
- opacity

Current effect kinds:

- Fade to black
- Vignette
- Grayscale

The current Advanced UI visualizes overlay/effect lanes. Full authoring controls for overlays/effects can continue to expand without changing the persistence/rendering boundary.

## Rendering integration

`ProjectRenderService` prefers the latest timeline version only when it matches the current storyboard and original Song IDs. A stale timeline is not silently applied to a newer storyboard/song.

The render manifest persists:

- `TimelineVersionId`
- source trim/rate/freeze settings
- transition metadata
- transforms/crop/color/opacity
- overlays/effects

All these values participate in the deterministic timeline SHA-256 hash.

`FfmpegProjectRenderEngine` consumes those values while still mapping only the protected original Song as output audio. Generated clip/overlay audio is never mapped into the final mix.

Current render application includes source trim, rate/freeze, transform/crop, color/opacity, fade transition behavior, overlays, and bounded effects. `Crossfade` is persisted as an explicit intent but currently uses fade behavior in deterministic rendering; a true neighboring `xfade` composition remains intentionally open rather than being claimed complete.

## Validation status

Repository-side tests have been added for:

- immutable timeline versions
- protected Song provenance
- split/reorder/variant replacement/version restore
- overlay/effect versioning
- Advanced FFmpeg argument construction and original-Song-only audio mapping
- frontend Advanced workspace/analysis/inspector source contract

These tests, typecheck, browser behavior, and actual FFmpeg filters have **not been claimed successful until they are executed locally**. See `TESTPLAN.md`.
