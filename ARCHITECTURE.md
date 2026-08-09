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
  |      +--> provider boundary contracts
  |      +--> persistence boundary contract
  |      +--> media-storage boundary contract
  |      +--> job-queue boundary contract
  |      +--> render-engine boundary contract
  |
  +--> Infrastructure
         (concrete adapters added in later PLAN blocks)
```

The backend can later split deployment units if a demonstrated scaling or operational requirement justifies it. Logical service boundaries must not be interpreted as a requirement to deploy microservices.

## Backend layers

### Domain

`OpenMusicVideoCreator.Domain`

Owns core domain concepts and invariants. It has no project references to outer layers.

The foundation does not yet contain project/storyboard/generation aggregates; those are introduced by later PLAN blocks.

### Application

`OpenMusicVideoCreator.Application`

Coordinates use cases and owns interfaces for external capabilities. The foundation establishes these seams:

- `IProviderCatalog`
- `IApplicationPersistence`
- `IMediaStorage`
- `IJobQueue`
- `IRenderEngine`

These are intentionally small bootstrap contracts. Later blocks may refine their data contracts as the corresponding domain models are implemented.

Application references Domain only.

### Infrastructure

`OpenMusicVideoCreator.Infrastructure`

Will implement DuckDB, filesystem/object storage, AI provider clients, job wake-up mechanisms, secrets, FFmpeg/ffprobe, clocks, and other external integrations.

At foundation stage it contains no concrete adapter because those implementations belong to later PLAN blocks.

Infrastructure references Application and therefore depends inward on the abstractions it implements.

### API host

`OpenMusicVideoCreator.Api`

Owns HTTP transport and process hosting. It currently provides:

- `/healthz`
- `/api/system/version`
- Development OpenAPI endpoint
- local-development CORS policy
- JSON console logging
- `X-Correlation-ID` middleware

Endpoints should remain thin. Product behavior belongs in Application use cases rather than controllers/minimal API delegates.

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
- demonstrate the typed API path

Persisted product/generation state will remain backend-authoritative. Future editor state may use local transient UI state, but must not replace durable backend state for projects/jobs/generations.

## API contract strategy

ASP.NET Core OpenAPI is the contract source.

`openapi-typescript` generates TypeScript types into:

```text
frontend/src/api/schema.d.ts
```

The committed bootstrap snapshot makes a fresh frontend build independent of a running backend. When API shapes change, developers run the backend and regenerate the snapshot.

Frontend request/response code should derive types from the generated schema. It should not create parallel handwritten DTO models for backend contracts.

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

Provider secrets must never be stored directly in DuckDB, project exports, logs, or committed configuration.

## Persistence and media

No persistence implementation exists yet.

The architectural rule already established is:

- DuckDB will store structured metadata
- large media blobs remain on filesystem/object storage
- Infrastructure implements those storage boundaries
- Application/Domain code does not depend on DuckDB APIs or filesystem paths

The concrete schema and media layout are Block 2 work.

## Jobs and remote generation

No job scheduler/worker implementation exists yet.

The application boundary makes job dispatch explicit so later generation work can be asynchronous and persisted. Persisted job state—not an in-memory queue—will be authoritative when Block 4 is implemented.

## Provider architecture

No paid AI provider is integrated yet.

Provider-independent capability interfaces and full provider metadata/error models are Block 3 work. The foundation contains only the provider-catalog seam so business logic does not start by depending on one vendor SDK.

## Rendering

No FFmpeg process is invoked yet.

`IRenderEngine` establishes the application boundary. Concrete FFmpeg/ffprobe execution belongs in the assembly/render PLAN blocks and must use typed arguments/process invocation rather than shell-command interpolation.

## Tests

Current automated test layers:

- frontend bootstrap contract test
- backend architecture dependency tests
- backend API integration tests

Paid providers are not required for any current test.

GitHub Actions runs frontend lint/typecheck/test/build and backend restore/build/test on pull requests and pushes to `main`.
