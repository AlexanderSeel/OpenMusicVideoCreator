# OpenMusicVideoCreator Architecture

This document describes architecture that exists in the repository today. Future capabilities remain unchecked in `PLAN.md` until implemented.

## Deployment shape

The MVP is a modular monolith / service-oriented application, not a distributed microservice system.

```text
Browser
  |
  v
Next.js / React frontend
  |
  | typed HTTP/JSON + SSE
  v
ASP.NET Core API host
  |
  +--> Application
  |      ProjectService / ProjectMediaService
  |      SongAnalysisService
  |      ProviderSettingsService
  |      JobService / JobProcessor
  |      repository, provider, media, analysis and render contracts
  |
  +--> Infrastructure
         DuckDB repositories
         local media storage/path resolver
         ffprobe metadata adapter
         streaming FFmpeg signal analyzer
         provider catalog + mock adapters
         credential resolver
         persistent job adapters
```

Logical boundaries are deliberately clean enough to split later if a real deployment/scaling requirement appears, but the current deployment remains one backend process.

## Dependency direction

```text
Domain
  ^
  |
Application
  ^
  |
Infrastructure

Application <--- API
Infrastructure <--- API
```

Domain contains no ASP.NET Core, DuckDB, FFmpeg, filesystem, or provider SDK dependencies. Application owns interfaces and use-case coordination. Infrastructure implements external capabilities. API maps HTTP only.

## Domain

Implemented domain areas include:

- `MusicVideoProject` / `ProjectDraft`
- output aspect ratio and generation preset
- project references including durable `Song` asset references
- media asset metadata
- persistent generation-job state model and state machine
- versioned `SongAnalysis`
- waveform buckets, energy points, beat markers and editable song sections
- derived bars, four-bar phrase windows and quiet ranges

`SongAnalysis.ValidateSections` enforces ordered, non-overlapping ranges within song duration. Edited Structure Maps create new analysis versions rather than mutating previous analysis.

Bars/phrases/quiet ranges are deterministic derivations from persisted beat/energy data, so redundant derived arrays do not need separate DuckDB storage.

## Application

Important application seams now include:

- project/settings/media repositories
- `IMediaStorage`
- `IMediaProbe`
- `IAudioSignalAnalyzer`
- `ISongAnalysisRepository`
- provider capability interfaces
- credential resolver/catalog
- persistent job repository/queue/change-stream/dispatcher
- render-engine boundary

### Project media

`ProjectMediaService` attaches a song to a project using the existing media-storage abstraction. Uploads are validated for safe filename, supported extension/MIME type, non-empty content, and the configured 512 MB limit.

Replacing a song creates a new media asset and changes the project `Song` reference. The previous media asset is not implicitly deleted.

### Song analysis

`SongAnalysisService`:

1. loads the authoritative project `Song` reference,
2. probes media metadata through `IMediaProbe`,
3. analyzes the signal through `IAudioSignalAnalyzer`,
4. creates waveform/energy/beat data and BPM estimate,
5. proposes editable sections using energy changes and duration constraints,
6. persists a new immutable analysis version.

Saving Structure Map edits creates another version with `UserEdited` section provenance while retaining the same source analysis data.

## Infrastructure

### DuckDB

DuckDB is authoritative for structured application metadata. Current schema version is **3**:

```text
schema_migrations
projects
project_targets
project_references
application_settings
project_settings
media_assets
jobs
job_dependencies
job_attempts
song_analyses
```

`song_analyses` stores source asset/version metadata plus waveform, energy, beats and sections as JSON columns. The table has a unique `(project_id, version)` constraint and project/version index.

### Media paths

`LocalMediaPathResolver` owns the configured project-media root and path-traversal checks. `LocalMediaStorage`, ffprobe and FFmpeg reuse this single path-resolution policy instead of duplicating filesystem rules.

Large media bytes remain outside DuckDB.

### ffprobe

`FfprobeMediaProbe` uses `ProcessStartInfo.ArgumentList`, never shell-command interpolation. It reads structured JSON for duration, codec, sample rate, channels and bitrate.

### FFmpeg signal analysis

`FfmpegAudioSignalAnalyzer` safely invokes FFmpeg with typed process arguments and streams mono 8 kHz signed 16-bit PCM from stdout. It does not decode the complete song into memory.

It produces:

- bounded waveform buckets with minimum/maximum/RMS
- normalized 50 ms energy points
- local-onset beat candidates with confidence
- BPM estimate from median beat intervals

Domain inference then derives four-beat bars, four-bar phrases and quiet regions.

Vocal/instrumental classification and transcription-assisted lyric timing are not implemented yet and remain unchecked in `PLAN.md`.

## API

Current project/analysis endpoints include:

```text
GET    /api/projects/
POST   /api/projects/
GET    /api/projects/{id}
PUT    /api/projects/{id}
DELETE /api/projects/{id}
GET    /api/projects/{id}/song
POST   /api/projects/{id}/song
GET    /api/projects/{projectId}/analysis/
POST   /api/projects/{projectId}/analysis/
GET    /api/projects/{projectId}/analysis/versions
PUT    /api/projects/{projectId}/analysis/sections
```

Provider and persistent-job APIs remain available as documented in `README.md`.

Public enums serialize as readable strings. Frontend contracts are derived from the committed OpenAPI TypeScript snapshot.

## Frontend

The current frontend includes a real Simple Mode product workflow:

```text
src/features/projects/
  ProjectStudio.tsx       orchestration
  ProjectSidebar.tsx      saved-project navigation
  ProjectForm.tsx         project/song/output inputs
  projectModel.ts         editor/request helpers

src/features/analysis/
  SongAnalysisPanel.tsx   analysis controls, waveform and Structure Map
```

Simple Mode intentionally hides provider IDs, model IDs, seeds and raw provider JSON.

`SongAnalysisPanel` shows:

- duration/BPM/sample-rate summary
- waveform
- beat and bar markers
- phrase spans
- quiet-range shading
- supplied lyrics lane
- editable section labels/types/start/end boundaries
- analysis version number

The supplied project lyrics remain authoritative text; current analysis does not modify them.

## Persistent jobs

Generation job state remains persisted in DuckDB. Normal resume does not regenerate completed work. Provider task IDs survive restart and move work into reconciliation instead of blind resubmission. In-memory job change broadcasts only wake/notify clients; persisted state remains authoritative.

## Security boundaries

- provider secrets are references, never plaintext DuckDB/project data
- upload filenames are validated as leaf names
- local media paths cannot escape configured storage root
- FFmpeg/ffprobe use argument lists rather than shell strings
- media bytes are not stored as DuckDB blobs
- successful generated/media assets are not silently overwritten/deleted

## Tests currently present

Repository tests cover, among other areas:

- architecture dependency direction
- project/persistence/media round trips
- path traversal protection
- project song attachment and non-destructive replacement
- provider catalog/settings/credential non-leakage/mock failures
- job state/retry/recovery/dependency/duplicate-claim behavior
- versioned song-analysis persistence
- invalid Structure Map overlap rejection
- beat → bar → phrase inference
- quiet-range inference
- frontend typed API contract structure
- Simple Mode provider-independence/accessibility structure
- waveform/Structure Map UI structure

The full local repository build/typecheck/test suite has not been executed in the current environment because repository checkout/network access is unavailable. FFmpeg/ffprobe command shapes were validated locally against a generated audio fixture without using GitHub Actions.
