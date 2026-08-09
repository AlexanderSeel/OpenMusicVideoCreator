# OpenMusicVideoCreator — Agent Development Rules

Read these files before changing implementation:

1. `AI_Music_Video_Studio_Master_Prompt.md` — product/domain source of truth.
2. `PLAN.md` — unfinished implementation work only.
3. `ARCHITECTURE.md` and `README.md` when they exist.
4. `SKILLS.md` for project-relevant agent skills.

## Work style

- Work autonomously from the next meaningful unfinished `PLAN.md` block when the task is clear.
- Implement **complete blocks/coherent vertical slices**, not a stream of tiny unrelated changes.
- A block is complete only when implementation, tests, documentation, and repository-side acceptance criteria are complete.
- Remove completed work from `PLAN.md`; do not leave checked-off history there.
- Never delete a PLAN item merely because code was started.
- Keep changes tightly scoped to the active block.
- Prefer simple solutions that satisfy the real requirement over speculative infrastructure.

## Clean-code rules

- Keep the codebase modular and reusable.
- Prefer focused components, classes, functions, hooks, and services with one clear responsibility.
- Extract shared behavior when it is genuinely reused or defines an important architectural boundary; do not create abstractions only to reduce line count.
- Avoid god-components, god-services, static utility dumping grounds, duplicated provider logic, duplicated DTO mapping, and business logic inside UI/controllers.
- Favor composition and explicit interfaces at external boundaries.
- Names must describe intent. Avoid generic names such as `Helper`, `Manager`, `Utils`, or `Common` when a domain-specific name exists.
- Keep public APIs small. Hide provider/FFmpeg/DuckDB details behind infrastructure adapters.
- Delete obsolete code when replacing it; do not leave parallel old/new implementations unless migration compatibility is required.

## Architecture guardrails

- The MVP is a modular monolith/service-oriented backend, not a distributed microservice estate.
- Domain/Application code must not depend on concrete AI provider SDKs, HTTP controllers, DuckDB implementation details, filesystem paths, FFmpeg processes, or frontend concerns.
- Provider adapters implement capability-based contracts. Never put provider-specific branching throughout business logic.
- Model/provider capabilities drive supported UI options; never assume every provider supports the same fields.
- Remote generation must be asynchronous and persisted. HTTP requests may submit/query work but never wait for long-running model generation to finish.
- Persisted job state is authoritative. In-memory channels/queues may be used only as wake-up/throughput mechanisms.
- All generation/editing is non-destructive. New generations create versions/variants and the selected variant is a reference.
- Preserve the original uploaded song. Rendering consumes it; editing does not mutate it.
- DuckDB stores structured metadata, not large audio/image/video blobs.
- Media operations use a typed FFmpeg/ffprobe abstraction. Never build shell command strings from untrusted input.
- Credential values must not be stored in DuckDB, source control, project exports, logs, or API responses.

## Frontend rules

- Use reusable editor primitives and design tokens rather than page-specific one-off UI.
- Keep Simple Mode approachable; provider IDs, seeds, raw JSON, retry thresholds, and model-specific settings belong in Advanced/Expert UI.
- Prefer server/domain state from the backend as the source of truth for persisted project/generation state.
- Keep timeline/storyboard rendering efficient for tens of scenes and many assets; avoid rerendering the whole editor for local scene changes.
- Keyboard interaction, focus visibility, labels, reduced-motion behavior, and usable tablet layouts are part of acceptance, not polish.

## Backend rules

- Controllers/endpoints are thin: validation/transport mapping only.
- Application use cases coordinate domain behavior and abstractions.
- Infrastructure implements DuckDB, filesystem/object storage, provider clients, FFmpeg, secrets, clocks, and background execution.
- Normalize provider errors once at the adapter boundary.
- Make state transitions explicit and testable; do not scatter job-status assignments across handlers.
- Make retries idempotent and bounded.
- Use cancellation tokens for I/O and long-running local media work.
- Structured logs include project/job/scene/provider identifiers where available, but never secrets or full sensitive provider payloads.

## Tests and validation

- Every repository-side behavior that can reasonably be automated should be covered.
- Use real temporary DuckDB databases for persistence integration tests.
- Mock paid AI providers for normal tests.
- Mock providers must cover: success, delayed completion, rate limiting, quota exhaustion, rejection, transient failure, permanent failure, and provider-side queued jobs.
- Test state transitions, recovery after restart, pause/resume, retries, dependency handling, prompt versioning, cost caps, and scene-level regeneration.
- Media/render tests should verify produced metadata with ffprobe when FFmpeg is available in CI.
- Never claim a command passed unless it actually executed successfully.

## Documentation discipline

- `PLAN.md`: unfinished work only.
- `ARCHITECTURE.md`: actual architecture and important decisions, not aspirational marketing.
- `README.md`: current setup, prerequisites, start/test/build commands, supported capabilities.
- Add ADRs only for decisions that are expensive to reverse or materially affect boundaries/data/contracts.

## Product invariants

These must remain true throughout development:

- AI does the first pass; the user can correct the important part without starting over.
- One bad scene never requires regenerating the whole video.
- Completed generations survive restart.
- Provider lock-in is avoided.
- Successful assets are never silently overwritten.
- Costs and retries are bounded and visible.
- The application can be closed and safely resumed.
- The original music track stays synchronized and protected.
