# Block 11 — Deterministic project rendering

Block 11 turns the selected scene-clip or Advanced timeline into persistent Preview and Final MP4 renders without mutating source audio or generated clip assets.

## Render provenance

`ProjectRenderService` builds an immutable `ProjectRenderManifest` from:

- the latest persisted `StoryboardVersionId`
- the project's original `SongMediaAssetId`
- the compatible `TimelineVersionId` when Advanced editing exists
- selected completed `SceneClipVariant`/media provenance
- exact timeline order and duration
- source trim, playback-rate, and freeze decisions
- transition kind/duration
- transform/crop/color/opacity decisions
- overlay, effect, and subtitle records
- output dimensions/profile

The manifest stores a SHA-256 timeline hash calculated from all content-affecting timeline decisions. Subtitle text is represented canonically as UTF-8 data in that hash, together with subtitle timing/style values. Preview and Final renders produced from an unchanged timeline therefore share the same timeline hash even though their encoding profiles differ.

Every render creates a new `ProjectRenderRecord`. Prior renders and outputs are retained.

## Persistent execution

Renders use the existing persistent job engine with job type:

```text
project.render
```

The render job depends on selected scene-clip generation jobs where those job IDs are available. HTTP only queues work; FFmpeg execution remains asynchronous in the background worker.

`ProjectRenderRecord` persists:

- immutable manifest
- render version
- persistent job ID
- output media asset ID
- render state
- latest deterministic FFmpeg command log
- current/latest error
- per-attempt history

Attempt history records start/completion times, outcome, command log, and error for each render execution. Automatic transient retries close the failed attempt but keep the render record queued. Retry exhaustion leaves the render failed and manually retryable. User cancellation synchronizes both the persistent job and render record; manual retry restarts the same job/manifest rather than creating a different timeline.

Active local execution is also connected to an ephemeral cancellation signal. Persisted job state remains authoritative, but a currently running FFmpeg process receives cancellation immediately so stale local completion cannot overwrite a persisted `Cancelled` state.

Render history is stored through `IProjectRenderRepository`; the current DuckDB adapter stores versioned JSON in project settings under `render.history.v1`.

## FFmpeg assembly

`FfmpegProjectRenderEngine` uses `ProcessStartInfo.ArgumentList`; paths are never assembled into a shell command.

Per-clip composition supports:

- source trim
- playback-rate adjustment
- final-frame freeze/padding
- scale/position
- crop
- opacity
- brightness/contrast/saturation
- fixed output frame rate/pixel format
- Cut
- Fade
- neighboring Crossfade through FFmpeg `xfade`

For a Crossfade, the outgoing clip is extended by exactly the incoming transition duration and `xfade` begins at the incoming clip's nominal timeline boundary. This preserves the original contiguous song/timeline duration instead of shortening the project.

After clip composition, the render graph can apply:

- bounded Fade-to-black/Vignette/Grayscale effect records
- timed image/video overlays with scale/position/opacity
- timed burned-in subtitles using `drawtext`

Subtitle text is escaped for FFmpeg's filter parser and uses `expansion=none`; text is never interpreted as shell input.

Generated clip and overlay audio are not included in the output mix. The only mapped audio stream is the project's original uploaded Song asset.

Current Preview encoding is deliberately faster/lower cost than Final. Final uses H.264 MP4 at the configured project resolution. Both profiles use the same content/timeline decisions.

## Post-render validation

The worker persists the assembled MP4 to temporary/output storage and then runs the existing `IMediaProbe`/ffprobe adapter before publishing a successful render record.

A render is rejected if:

- the MP4 duration differs from the manifest outside the bounded frame-based tolerance
- no valid audio stream is present

An output that fails validation is removed before a completed render record is published. Cancellation/failure cleanup also removes partially stored output and any just-created media metadata.

Successful outputs receive `MediaAssetMetadata` with `MediaCreationSource.Rendered` and remain downloadable through a range-enabled API endpoint.

## API

```text
GET  /api/projects/{projectId}/renders/
POST /api/projects/{projectId}/renders/
GET  /api/projects/{projectId}/renders/{renderId}
POST /api/projects/{projectId}/renders/{renderId}/cancel
POST /api/projects/{projectId}/renders/{renderId}/retry
GET  /api/projects/{projectId}/renders/{renderId}/output
```

## Frontend

`ProjectRenderWorkspace` exposes:

- Render Preview
- Render Final MP4
- versioned render history
- state and error display
- timeline/storyboard/original-Song provenance
- render-attempt history
- cancel while active
- retry failed/cancelled render using the same manifest
- completed output download
- deterministic FFmpeg command disclosure

Advanced composition authoring lives in Block 12 rather than being hidden in render-specific controls.

## Validation status

Repository-side tests cover render planning, timeline/source provenance, lifecycle/attempt history, cancellation/retry, true Crossfade argument construction, subtitle escaping/timing, and Advanced FFmpeg composition arguments. Frontend source tests cover the mounted render and Advanced timeline workflows.

These tests and real FFmpeg/ffprobe execution are **not considered passed until they are actually executed**. `TESTPLAN.md` remains the source of truth for executable validation.
