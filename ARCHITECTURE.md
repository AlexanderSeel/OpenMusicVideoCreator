# OpenMusicVideoCreator Architecture

This document describes the architecture that exists in the repository today. Future capabilities belong in `PLAN.md` until implemented.

## Deployment shape

The MVP is a **modular monolith / service-oriented application**, not a distributed microservice system.

```text
Browser
  |
  v
Next.js frontend
  |
  | HTTP / JSON, typed from OpenAPI
  v
ASP.NET Core API host
  |
  +--> Application
  |      +--> ProjectService
  |      +--> repository/storage contracts
  |      +--> provider boundary contracts
  |      +--> job-queue boundary contract
  |      +--> render-engine boundary contract
  |
  +--> Infrastructure
         +--> DuckDB repositories
         +--> local filesystem media storage
         +--> future external adapters
```

The backend can later split deployment units if a demonstrated scaling or operational requirement justifies it. Logical service boundaries must not be interpreted as a requirement to deploy microservices.

## Backend layers

### Domain

`OpenMusicVideoCreator.Domain`

Owns core domain concepts and invariants. It has no project references to outer layers.

Implemented domain concepts now include:

- `MusicVideoProject`
- `ProjectDraft`
- output aspect ratio and resolution
- generation preset choice
- project reference IDs/kinds
- media asset metadata and creation source

Project validation belongs in the domain model: title/resolution/budget invariants are checked before persistence.

### Application

`OpenMusicVideoCreator.Application`

Coordinates use cases and owns interfaces for external capabilities. Current important seams include:

- `IProjectRepository`
- `IApplicationSettingsRepository`
- `IProjectSettingsRepository`
- `IMediaAssetRepository`
- `IApplicationPersistence`
- `IMediaStorage`
- `IProviderCatalog`
- `IJobQueue`
- `IRenderEngine`

`ProjectService` owns project CRUD coordination plus versioned portable JSON export/import. DuckDB and filesystem details do not appear in Application code.

Application references Domain only.

### Infrastructure

`OpenMusicVideoCreator.Infrastructure`

Current concrete adapters:

- `DuckDbDatabase` — schema bootstrap/migration version tracking
- `DuckDbProjectRepository`
- `DuckDbSettingsRepository`
- `DuckDbMediaAssetRepository`
- `LocalMediaStorage`

DuckDB access uses the ADO.NET provider through `DuckDB.NET.Data.Full`. Every operation opens a short-lived connection from `DuckDbConnectionFactory` rather than holding application state only in memory.

`LocalMediaStorage` implements the media boundary without exposing filesystem paths to Domain/Application code. It creates deterministic project directories, uses unique stored file names, calculates SHA-256, and rejects path traversal.

Infrastructure references Application and therefore depends inward on the abstractions it implements.

### API host

`OpenMusicVideoCreator.Api`

Owns HTTP transport and process hosting. It currently provides:

- `/healthz`
- `/api/system/version`
- project CRUD endpoints
- project portable export/import endpoints
- Development OpenAPI endpoint
- local-development CORS policy
- JSON console logging
- `X-Correlation-ID` middleware
- infrastructure composition and DuckDB initialization

Endpoints map transport DTOs to Application/Domain inputs and remain free of DuckDB SQL or filesystem logic.

## Dependency direction

Allowed compile-time direction:

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

Forbidden directions include:

- Domain → Application / Infrastructure / API
- Application → Infrastructure / API
- Infrastructure → API

`OpenMusicVideoCreator.ArchitectureTests` checks these rules using assembly references. Project references also encode the intended direction.

## Frontend

The frontend is a Next.js 16 / React 19 TypeScript application.

Current responsibilities are deliberately small:

- render the application shell
- call the bootstrap backend endpoint
- hold the generated OpenAPI TypeScript snapshot, now including project endpoints

The project-management UI itself belongs to a later Simple Mode block. Persisted product/generation state remains backend-authoritative.

## API contract strategy

ASP.NET Core OpenAPI is the contract source.

`openapi-typescript` generates TypeScript types into:

```text
frontend/src/api/schema.d.ts
```

The committed snapshot makes a fresh frontend build independent of a running backend. When API shapes change, developers run the backend and regenerate the snapshot.

Public project enums are serialized as readable strings. Frontend request/response code should derive types from the generated schema instead of creating parallel handwritten DTO models.

## Logging and correlation

The API writes structured JSON console logs through the standard .NET logging abstractions.

Every request receives `X-Correlation-ID`:

- an incoming value is preserved when supplied
- otherwise a trace ID or generated identifier is used
- the value is added to the response
- the value is added to the logging scope

Future project/job/scene/provider operations should add their identifiers to logging scopes without logging secrets or sensitive provider payloads.

## Configuration

Configuration follows normal ASP.NET Core and Next.js conventions:

- `appsettings.json` for safe backend defaults
- `appsettings.Development.json` for local development values
- environment variables for deployment overrides and future credential references
- `frontend/.env.local` for local frontend overrides, excluded from source control

Current storage keys:

```text
Storage:DatabasePath
Storage:ProjectsRoot
```

Environment-variable equivalents use ASP.NET Core's double-underscore convention.

Provider secrets must never be stored directly in DuckDB, project exports, logs, or committed configuration.

## Persistence

DuckDB is the authoritative metadata store for the running application.

Schema version 1 currently contains:

```text
schema_migrations
projects
project_targets
project_references
application_settings
project_settings
media_assets
```

Project target/reference order is persisted explicitly. Project updates replace only their mutable project metadata/collections; project settings and media asset records are not silently deleted during normal project edits.

Deleting a project removes its project row, target/reference rows, and project-scoped settings. Media asset metadata and media files are intentionally not implicitly destroyed by project/reference edits because generated work is treated non-destructively.

## Media storage

Large audio/image/video bytes do not live in DuckDB.

Default filesystem layout:

```text
projects/{project-id}/
  source/
  references/
    characters/
    styles/
    locations/
  analysis/
  keyframes/
  generated/
  proxies/
  renders/
```

`LocalMediaStorage.SaveAsync`:

- accepts a project ID and controlled storage area
- accepts only a safe leaf file name
- creates a unique stored file name rather than overwriting an existing asset
- writes bytes to the configured projects root
- calculates SHA-256
- returns relative location + size + checksum

`media_assets` stores metadata only: location, checksum, MIME type, dimensions, duration, file size, source, timestamps, and optional project association.

## Portable project documents

`ProjectService` exports a versioned portable JSON document containing the supported project metadata and reference IDs.

The format is intended for interchange/backup. It is not a second runtime source of truth: DuckDB remains authoritative while the application is running.

Import validates the document version before upserting its project metadata.

## Jobs and remote generation

No persistent job scheduler/worker implementation exists yet.

The application boundary makes job dispatch explicit so later generation work can be asynchronous and persisted. Persisted job state—not an in-memory queue—will be authoritative when the corresponding PLAN block is implemented.

## Provider architecture

No paid AI provider is integrated yet.

Provider-independent capability interfaces and full provider metadata/error models are the next major architecture block. The current provider-catalog seam prevents business logic from starting with one vendor SDK baked in.

## Rendering

No FFmpeg process is invoked yet.

`IRenderEngine` establishes the application boundary. Concrete FFmpeg/ffprobe execution belongs in later media/render blocks and must use typed arguments/process invocation rather than shell-command interpolation.

## Tests

Current automated test layers include:

- frontend typed-contract snapshot test
- backend architecture dependency tests
- health/version API integration tests
- project CRUD/export/import API integration test
- real temporary DuckDB project round-trip/restart test
- application/project settings persistence test
- media metadata/storage separation test
- portable project export/import round-trip test
- non-destructive project-reference update test
- media path-traversal rejection test

Paid providers are not required for normal tests.

GitHub Actions runs frontend lint/typecheck/test/build and backend restore/build/test on pull requests and pushes to `main`. For direct `main` work the workflow also publishes a standard `ci/combined` commit status after both jobs finish.
