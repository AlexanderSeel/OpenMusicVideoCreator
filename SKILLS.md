# OpenMusicVideoCreator — Agent Skills

These skills are development aids for coding agents working on this repository. They are **not runtime dependencies** of OpenMusicVideoCreator and must not dictate product architecture when they conflict with `AI_Music_Video_Studio_Master_Prompt.md`, `AGENTS.md`, or `PLAN.md`.

Install them with the `skills` CLI from the repository root. Re-run installation periodically when intentionally updating agent guidance; do not silently churn vendored skill content during unrelated feature work.

## Core skills

### 1. Music-to-video

```bash
npx skills add https://github.com/heygen-com/hyperframes --skill music-to-video
```

Use for music-driven pacing, beat/phrase analysis concepts, storyboard timing, and music-video workflow ideas. Reuse applicable concepts; do **not** couple OpenMusicVideoCreator's domain or rendering architecture to HyperFrames.

### 2. .NET clean architecture

```bash
npx skills add https://github.com/codewithmukesh/dotnet-claude-kit --skill clean-architecture
```

Use for ASP.NET Core/C# dependency direction, domain/application/infrastructure separation, thin API endpoints, and testable provider/persistence boundaries.

### 3. Microservices/service-boundary patterns

```bash
npx skills add https://github.com/wshobson/agents --skill microservices-patterns
```

Use for service boundaries, resilience, retry/backoff, and asynchronous integration patterns. **Guardrail:** the MVP remains a modular monolith/service-oriented single backend process unless a concrete deployment requirement justifies distributed services.

### 4. Tailwind v4 design system

```bash
npx skills add https://github.com/wshobson/agents --skill tailwind-design-system
```

Use for Tailwind CSS v4, reusable design tokens, variants, responsive editor UI, and accessibility-oriented component patterns.

The originally considered command below is intentionally not used:

```bash
# NOT VALID AS WRITTEN — the repository does not expose this skill name
npx skills add https://github.com/mastra-ai/mastra --skill tailwind-best-practices
```

`mastra-ai/mastra` currently contains a skill named `tailwind-v4`, not `tailwind-best-practices`. `tailwind-design-system` is used here because it is directly targeted at production Tailwind v4 component/design-system work.

### 5. React / Next.js best practices

```bash
npx skills add https://github.com/vercel-labs/agent-skills --skill vercel-react-best-practices
```

Use for React/Next.js data-flow, rendering, performance, bundle, and component design guidance—especially important for storyboard, waveform, queue, and timeline views.

### 6. Web interface guidelines

```bash
npx skills add https://github.com/vercel-labs/agent-skills --skill web-design-guidelines
```

Use as a review skill for accessibility, interaction, focus behavior, layout, forms, dialogs, tables, responsive behavior, and general editor UX quality.

### 7. Prompt engineering patterns

```bash
npx skills add https://github.com/wshobson/agents --skill prompt-engineering-patterns
```

Use for AI Director prompts, structured outputs, reusable prompt templates, validation/recovery, prompt versioning, and provider-independent prompt construction.

Never use hidden chain-of-thought as a product contract. Persist user-facing intent, structured outputs, prompts, versions, and evaluation results instead.

### 8. Error handling patterns

```bash
npx skills add https://github.com/wshobson/agents --skill error-handling-patterns
```

Use for normalized provider errors, recoverable/permanent failure classification, retries, graceful degradation, cancellation, and consistent frontend/API error handling.

### 9. Video processing and FFmpeg editing

```bash
npx skills add https://github.com/erichowens/some_claude_skills --skill video-processing-editing
```

Use for FFmpeg-based deterministic assembly, trim/concat/transitions/overlays/subtitles/audio muxing, export, and media pipeline practices.

**Security rule:** examples from any media skill must still pass the repository's safe-process-execution rules. Never interpolate untrusted filenames, prompts, or user values into a shell command string.

## Install all core skills

PowerShell:

```powershell
./scripts/install-skills.ps1
```

Bash:

```bash
./scripts/install-skills.sh
```

## Skill precedence

When guidance conflicts, use this precedence:

1. User's current explicit request
2. `AI_Music_Video_Studio_Master_Prompt.md`
3. `AGENTS.md`
4. `PLAN.md`
5. Actual repository architecture and tests
6. Installed third-party skills

A skill is advisory domain knowledge, not authority to rewrite the product into its preferred framework or architecture.
