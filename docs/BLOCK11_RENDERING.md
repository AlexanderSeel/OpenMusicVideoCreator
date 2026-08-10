# Block 11 — Deterministic project rendering

Block 11 turns the selected scene-clip timeline into persistent Preview and Final MP4 renders without mutating source audio or generated clip assets.

## Render provenance

`ProjectRenderService` builds an immutable `ProjectRenderManifest` from:

- the latest persisted `StoryboardVersionId`
- the project's original `SongMediaAssetId`
- exactly one selected completed `SceneClipVariant` per storyboard scene
- each selected clip's generated media asset ID
- storyboard order and exact scene duration
- scene transition metadata
- output dimensions/profile

The manifest stores a SHA-256 timeline hash calculated from storyboard ID, Song asset ID, selected clip/asset IDs, timing, and transition metadata. Preview and Final renders produced from unchanged scene selections therefore share the same timeline hash even though their encoding profiles differ.

Every render creates a new `ProjectRenderRecord`. Prior renders and outputs are retained.

## Persistent execution

Renders use the existing persistent job engine with job type:

```text
project.render
```

The render job depends on the selected scene-clip generation jobs where those job IDs are available. HTTP only queues work; FFmpeg execution remains asynchronous in the background worker.

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

Render history is stored through `IProjectRenderRepository`; the current DuckDB adapter stores versioned JSON in project settings under `render.history.v1`.

## FFmpeg assembly

`FfmpegProjectRenderEngine` uses `ProcessStartInfo.ArgumentList`; paths are never assembled into a shell command.

For every selected scene clip it applies deterministic video operations:

- pad a short provider clip by cloning its final frame when necessary
- trim to the storyboard scene duration
- reset timestamps
- scale to fill the configured project canvas
- crop to the exact output dimensions
- normalize frame rate
- normalize pixel format
- apply the currently supported basic fade-in transition where requested
- concatenate scene videos in storyboard order

Generated clip audio is not included in the output mix. The only mapped audio stream is the project's original uploaded Song asset.

Current Preview encoding is deliberately faster/lower cost than Final. Final uses H.264 MP4 at the configured project resolution. Both profiles use the same source timeline decisions.

## Post-render validation

The worker persists the assembled MP4 to temporary/output storage and then runs the existing `IMediaProbe`/ffprobe adapter before publishing a successful render record.

A render is rejected if:

- the MP4 duration differs from the manifest outside the bounded frame-based tolerance
- no valid audio stream is present

An output that fails validation is removed before `MediaAssetMetadata` or a completed render record is published.

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

## Remaining Block 11 scope

The deterministic render foundation is implemented. Richer timeline editing features such as true crossfades, overlays, subtitle/effect lanes, and user-adjustable transition composition are intentionally still open and align with Block 12's Advanced timeline editor rather than being embedded as hidden one-off render behavior.

## Validation status

Repository-side test code covers render planning, timeline/source provenance, lifecycle/attempt history, cancellation/retry, and FFmpeg argument construction. Frontend source tests cover the mounted render workspace and lifecycle controls.

These tests and real FFmpeg/ffprobe execution are **not considered passed until they are actually executed**. `TESTPLAN.md` remains the source of truth for executable validation.
