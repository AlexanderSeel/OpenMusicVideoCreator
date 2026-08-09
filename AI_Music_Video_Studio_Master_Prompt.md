# Master Development Prompt — AI Music Video Studio

Build a professional but easy-to-use **AI Music Video Studio** that turns a song, lyrics, storyline and reusable visual references into a complete AI-generated music video.

The application must optimize for two different workflows:

**Simple Mode**
- fast guided workflow
- minimal technical decisions
- automatic recommendations
- presets:
  - Fast
  - Balanced
  - Best Quality
  - Cheapest
  - Custom
- suitable for generating an entire video with only a few inputs

**Advanced Mode**
- editable song structure
- storyboard
- per-scene generation settings
- waveform/timeline
- individual clip regeneration
- AI/provider overrides
- prompt editing
- continuity controls
- lightweight professional video editing

Do **not** try to become Premiere/DaVinci Resolve. The advanced editor should specialize in AI music-video generation and correction.

## 1. Primary user workflow

The default project workflow is:

```text
Create Project
   ↓
Upload Song
   ↓
Enter/Paste Lyrics
   ↓
Enter optional Storyline / Meaning / Direction
   ↓
Select or Create Characters
   ↓
Select Style
   ↓
Select Locations
   ↓
Choose Fast / Balanced / Best / Cheapest / Custom
   ↓
Analyze Song
   ↓
Generate Story / Visual Arc
   ↓
Generate Editable Storyboard
   ↓
Review
   ↓
Generate Keyframes
   ↓
Review / Adjust
   ↓
Animate Scenes
   ↓
Review / Regenerate individual scenes
   ↓
Automatic Assembly
   ↓
Timeline Fine-Tuning
   ↓
Render / Export
```

Every automated step must remain editable.

Never force the user to regenerate the whole video because one scene is bad.

---

## 2. Project creation

Each project should support:

- title
- artist
- audio file
- lyrics
- optional timed lyrics
- free-text storyline
- song meaning
- visual direction
- mood
- genre
- desired aspect ratio
- desired resolution
- target platforms
- character references
- style references
- location references
- additional image/video references
- global negative prompt
- generation preset
- estimated budget
- maximum budget

Supported targets should initially include:

```text
16:9  YouTube
9:16  TikTok / Reels / Shorts
1:1   social media
```

Allow creating multiple output variants from the same project without regenerating everything unnecessarily.

---

## 3. Automatic music analysis

Analyze the uploaded song automatically.

Detect or estimate:

- duration
- BPM
- beat positions
- bars
- musical phrases
- intro
- verses
- pre-choruses
- choruses
- bridge
- breakdown
- build-ups
- drops
- outro
- energy curve
- major dynamic changes
- quiet sections
- vocal sections
- instrumental sections

Display the result as an editable **Structure Map**.

Example:

```text
00:00 Intro
00:14 Verse 1
00:42 Chorus
01:05 Verse 2
01:34 Chorus
01:57 Bridge
02:18 Final Chorus
02:49 Outro
```

Show:

- audio waveform
- beat markers
- bar markers
- lyrics
- section boundaries
- scene boundaries

Everything must be manually adjustable.

Lyrics entered by the user should be alignable to the actual vocals.

Automatic transcription can assist alignment, but supplied lyrics are the authoritative text.

---

## 4. AI Director

Create an AI Director responsible for turning:

```text
song analysis
+ lyrics
+ storyline
+ visual style
+ characters
+ locations
```

into a coherent visual story.

The Director should understand the difference between:

- literal lyric interpretation
- symbolic interpretation
- cinematic narrative
- performance video
- abstract visual video
- mixed storytelling

Expose a control:

```text
Literal ←────────────→ Symbolic
```

Also expose:

- narrative strength
- abstraction
- emotional intensity
- darkness
- warmth
- surrealism
- realism
- visual complexity
- character acting intensity
- camera energy

The Director generates an overall **Visual Arc** before individual scenes.

Example:

```text
Beginning
Character is fully present.
Closed environments.
Low movement.
Cold atmosphere.

Middle
Relationships become strained.
Doors/windows begin opening.
More movement.
Character begins separating.

Ending
Environment becomes warmer.
Other characters become independent.
Main character progressively disappears.
Final frame continues without him.
```

The Visual Arc must remain visible and editable.

---

## 5. Storyboard

Automatically divide the song into shots.

Typical target:

```text
3-minute song
≈ 20–35 shots
≈ 4–10 seconds per AI-generated clip
```

Do not rigidly enforce those values.

Cuts should prefer musical phrases, beats and transitions rather than arbitrary equal-length segments.

Each storyboard scene must have:

- scene number
- start time
- end time
- duration
- song section
- associated lyric
- scene purpose
- characters
- location
- action
- emotion
- composition
- camera shot
- camera movement
- lighting
- environment motion
- visual symbolism
- continuity requirements
- start keyframe
- optional end keyframe
- prompt
- negative prompt
- selected provider/model
- generation status
- generation variants
- estimated cost
- actual cost

Display scenes visually as storyboard cards.

Support drag/drop reorder where timing allows.

---

## 6. Character Library

Implement a reusable **Character Library** independent of individual projects.

A character contains:

- name
- description
- role
- primary reference images
- optional face close-ups
- front view
- side view
- full-body reference
- characteristic features
- hairstyle
- skin details
- tattoos
- accessories
- clothing
- body proportions
- age appearance
- forbidden changes
- negative characteristics
- optional alternate outfits

Allow characters to be selected **when creating the project**.

Add:

```text
☑ Maintain Character Continuity
```

When enabled, every relevant scene should automatically receive character references and consistency instructions.

Support:

```text
Identity Lock
Appearance Lock
Wardrobe Lock
```

at different strengths.

A character can evolve during a video while retaining identity.

Example:

```text
Scene 1:
same character, intact

Scene 12:
same character, dirty clothing

Scene 22:
same character, partially dissolving

Scene 28:
same character, almost transparent
```

These are state changes, not new characters.

---

## 7. Character progression

Support project-wide character properties that can change over time.

Example:

```text
presence:
1.0 → 0.0

disintegration:
0.0 → 1.0

confidence:
0.3 → 0.9

isolation:
0.2 → 1.0
```

Expose these as curves across the song timeline.

The AI Director can create curves automatically.

Users can manually change them.

Scene prompts should inherit interpolated values.

This mechanism should be generic enough for:

- aging
- injury
- transformation
- disappearing
- clothing changes
- emotional changes
- corruption
- becoming brighter/darker
- environmental changes

---

## 8. Style Library

Implement reusable styles.

A Style contains:

- name
- description
- reference images
- visual prompt
- negative prompt
- realism
- color characteristics
- contrast
- grain
- lens behavior
- camera style
- lighting
- animation characteristics

Examples:

```text
Dark Neon Cinematic
Dreamlike Mystic
Realistic Film
Anime
Painterly
Cyberpunk
Vintage Film
Minimal Surreal
```

Never make the application dependent on hard-coded styles.

---

## 9. Location Library

Implement reusable Locations.

Each location contains:

- name
- description
- reference images
- visual constraints
- environmental details
- lighting presets
- weather
- time of day

Examples:

```text
Apartment
Rainy Street
Forest
Train Station
Rooftop
Empty Warehouse
Beach
```

Locations should maintain visual continuity across separated scenes.

---

## 10. AI Provider architecture

Create a **provider-independent abstraction layer**.

Do NOT build business logic directly against one AI API.

Define capability interfaces roughly equivalent to:

```text
ITextGenerationProvider
IImageGenerationProvider
IImageEditingProvider
IVideoGenerationProvider
IImageToVideoProvider
IVideoToVideoProvider
ILipSyncProvider
IUpscaleProvider
ITranscriptionProvider
IVisionEvaluationProvider
```

Providers advertise capabilities.

The UI must only show settings supported by the selected provider/model.

For example:

```text
supportsReferenceImages
supportsStartFrame
supportsEndFrame
supportsVideoReference
supportsNegativePrompt
supportsSeed
supportsNativeAudio
supportsImageToVideo
supportsVideoToVideo
maxReferences
minDuration
maxDuration
supportedAspectRatios
supportedResolutions
```

Do not assume all models expose the same properties.

---

## 11. Initial AI provider support

Design adapters so additional providers can be added easily.

Initial targets should include where their APIs support the required capability:

### Story / Director / Prompt creation
- OpenAI
- Google Gemini
- Anthropic
- optional local/OpenAI-compatible endpoint

### Image / Keyframe
- OpenAI image generation
- Google image generation
- Runway image generation
- other providers through adapters

### Video
- Runway
- Google Veo
- Luma
- additional providers via adapters

Never depend on a static list of model names.

Provider adapters should expose available models dynamically when APIs permit it, otherwise maintain provider-specific model catalogs that can be updated independently of application releases.

---

## 12. Generation presets

Implement:

### Fast

Optimize for:

- shortest generation time
- fast models
- fewer candidate variants
- lower QA threshold
- lower generation resolution where appropriate

### Balanced

Optimize for:

- quality
- speed
- reasonable cost
- good continuity

This should be the default.

### Best Quality

Optimize for:

- strongest available models
- multiple candidate clips
- stronger continuity checking
- higher resolution
- more automatic retries
- best output rather than cost

### Cheapest

Optimize for:

- lowest predicted cost
- cheaper models
- fewer variants
- reuse generated material when possible
- avoid unnecessary regeneration

### Custom

Every stage can be configured separately.

Example:

```text
Director:        OpenAI
Storyboard:      Gemini
Keyframes:       OpenAI Image
Animation:       Runway
Complex scenes:  Veo
Upscaling:       provider X
QA:              multimodal model Y
```

---

## 13. Smart model routing

Presets should not simply map an entire project to one provider.

Build a **Model Router**.

It considers:

- required capabilities
- requested quality
- expected cost
- scene complexity
- reference requirements
- current provider availability
- provider quota
- recent failures
- estimated generation time

Example:

```text
simple environmental shot
→ cheap/fast model

important character close-up
→ continuity-focused model

complex transformation
→ highest-quality model

provider unavailable
→ fallback provider if permitted
```

Custom mode allows disabling automatic fallback.

---

## 14. Provider settings

Settings screen per provider:

- enabled
- API credential reference
- models
- default model
- concurrency
- timeout
- retry count
- cost preferences
- allowed operations
- priority
- fallback priority

API keys must **not be stored as plaintext inside DuckDB**.

Use:

- OS credential store / keychain
- environment variables
- external secret provider

DuckDB stores only the credential reference.

---

## 15. Async generation architecture

All remote generation must be asynchronous.

Never block an HTTP request waiting for a video model to finish.

Use persisted Jobs.

Example hierarchy:

```text
ProjectGeneration
 ├─ StoryJob
 ├─ KeyframeJob 01
 ├─ VideoJob 01
 ├─ KeyframeJob 02
 ├─ VideoJob 02
 └─ FinalRenderJob
```

Job state must survive:

- application restart
- backend restart
- browser closure
- network interruption

---

## 16. Job status state machine

Use explicit states such as:

```text
Draft
Queued
Submitting
ProviderQueued
Generating
Downloading
Validating
Completed

Paused
WaitingForQuota
WaitingForProvider
WaitingForDependency

RetryScheduled
Rejected
FailedRetryable
FailedPermanent
Cancelled
```

Record:

- created time
- started time
- completed time
- provider
- model
- provider task ID
- retry count
- last response
- normalized error
- next retry time
- estimated cost
- actual cost

---

## 17. Pause / resume / restart

Support:

```text
Pause Project
Resume Project
Pause Scene
Retry Scene
Restart Scene
Cancel Scene
Retry All Failed
```

Pause must stop launching new jobs.

Do not corrupt already-running provider jobs.

When resumed, continue from the persisted generation state.

Never regenerate completed scenes unless explicitly requested.

---

## 18. Provider rejection and quota handling

Provider failures must be classified.

Distinguish at minimum:

```text
rate limit
temporary provider outage
monthly/daily quota exceeded
credits exhausted
authentication failure
content moderation rejection
invalid parameters
unsupported model
network failure
provider timeout
permanent generation failure
```

Do not treat every provider error as "Failed".

Example:

```text
Credits exhausted
→ WaitingForQuota

HTTP rate limit
→ RetryScheduled

Temporary outage
→ WaitingForProvider

Safety rejection
→ Rejected

Bad API key
→ FailedPermanent
```

For rate limits where a reset time is available, schedule a retry accordingly.

For exhausted credits where no reliable reset is known:

```text
WaitingForQuota
```

and provide:

```text
Retry Now
Resume automatically
Change Provider
Change Model
```

Allow periodic provider availability checks.

When resources become available again, queued work can continue automatically.

---

## 19. Generation Queue UI

Provide a global queue view.

Example:

```text
SCENE       PROVIDER    STATUS                PROGRESS

Scene 07    Runway      Completed             100%
Scene 08    Runway      Generating             62%
Scene 09    Veo         Provider queued
Scene 10    Runway      Waiting for quota
Scene 11    —           Waiting for Scene 10
```

Display:

- provider
- model
- status
- elapsed time
- retries
- cost
- errors
- actions

Updates should arrive live through SSE or WebSockets.

---

## 20. Generation variants

Allow multiple variants of a scene:

```text
Scene 08

A ★ selected
B
C
```

Users can:

- preview
- compare
- select
- delete
- regenerate

Only the selected variant appears in the final timeline.

---

## 21. AI quality control

After image/video generation, optionally perform automatic QA.

Evaluate:

- character identity
- wardrobe continuity
- location continuity
- prompt adherence
- unwanted characters
- anatomical problems
- major visual artifacts
- transition compatibility
- start/end frame compatibility

Generate a score.

Example:

```text
Character similarity     94
Scene adherence          91
Visual quality           86
Continuity               93

Overall                   91
```

Depending on preset:

```text
Fast
→ little/no automatic regeneration

Balanced
→ regenerate clearly failed shots

Best Quality
→ stricter threshold + multiple candidates

Cheapest
→ flag problems instead of automatically spending again
```

Never enter infinite regeneration loops.

---

## 22. Advanced timeline editor

The advanced editor should be available through:

```text
Advanced Editor
```

rather than being the default interface.

Include:

- waveform
- song structure
- lyrics lane
- beat/bar markers
- storyboard clips
- video preview
- scene boundaries
- transitions
- overlays
- effect tracks

Keep the original music track protected by default.

Support:

- trim clip
- move clip
- split clip
- replace clip
- regenerate clip
- extend clip
- change playback speed slightly
- freeze frame
- crossfade
- fade
- cut
- simple transform
- crop
- opacity
- basic color adjustments

Do not implement a complete conventional NLE.

---

## 23. Scene Inspector

Selecting a scene opens a professional inspector.

Sections:

### Story
- purpose
- lyric
- story action

### Character
- selected actors
- emotion
- action
- continuity
- state

### Environment
- location
- weather
- lighting

### Camera
- framing
- angle
- movement
- lens feel

### Generation
- provider
- model
- duration
- seed
- references
- start frame
- end frame
- negative prompt
- provider-specific settings

### Prompt

Show both:

```text
Director Intent
```

and

```text
Final Provider Prompt
```

Allow editing either.

Provide:

```text
Regenerate Prompt
```

without immediately spending generation credits.

---

## 24. Prompt history

Never lose prompts.

Store every version:

```text
Prompt v1
Prompt v2
Prompt v3
```

Record which prompt generated which asset.

This is essential for reproducibility.

---

## 25. DuckDB

Use **DuckDB as the persistent project/application database**.

Store structured metadata including:

- projects
- application settings
- project settings
- characters
- character references
- character states
- styles
- locations
- assets
- songs
- song analyses
- lyrics
- lyric timing
- song sections
- scenes
- scene versions
- prompts
- generations
- generation attempts
- jobs
- provider task IDs
- model configurations
- presets
- costs
- errors
- render jobs
- export history

Do not store large video/audio/image blobs directly in DuckDB.

Store media on disk/object storage and maintain:

- path/URI
- checksum
- MIME type
- width
- height
- duration
- file size
- creation source

inside DuckDB.

---

## 26. Suggested project storage

Use a clean structure such as:

```text
data/
  app.duckdb

projects/
  {project-id}/
    source/
      song.mp3
      lyrics.txt

    references/
      characters/
      styles/
      locations/

    analysis/
      waveform.json
      structure.json

    keyframes/
      scene-001/
      scene-002/

    generated/
      scene-001/
        variant-a.mp4
        variant-b.mp4

    proxies/

    renders/

    project.json
```

`project.json` should be an optional portable/exportable representation.

DuckDB remains authoritative for the running application.

---

## 27. Asset Library

Create a unified Asset Library.

Types:

```text
Character
Style
Location
Reference Image
Generated Image
Generated Video
Overlay
Effect
```

Assets should support:

- tags
- search
- preview
- favorites
- reuse across projects
- source tracking

---

## 28. Cost management

Generation cost is important.

Track per:

- project
- scene
- generation
- provider
- model

Show:

```text
Estimated project cost
Actual project cost
Remaining configured budget
```

Before expensive operations show an estimate when possible.

Allow project budget:

```text
€10
€25
€50
Unlimited
```

When approaching the cap, pause generation instead of silently exceeding it.

---

## 29. Render engine

Use FFmpeg for deterministic final composition.

AI services generate assets.

FFmpeg performs:

- clip assembly
- timing
- audio muxing
- transitions
- scaling
- cropping
- fades
- text/subtitles
- overlays
- final encoding

Final audio must use the original uploaded song unless explicitly changed.

Provide preview/proxy rendering before expensive final output.

---

## 30. Rendering outputs

Initially support:

```text
H.264 MP4
1080p

16:9
9:16
1:1
```

Architect rendering so 4K can be added cleanly.

Allow:

```text
Preview Render
Final Render
```

Preview renders should be fast and inexpensive.

---

## 31. Application architecture

Use a service-oriented architecture.

Recommended logical components:

```text
Web UI
   │
   ▼
Application API
   │
   ├── Project Service
   ├── Asset Service
   ├── Music Analysis Service
   ├── Director Service
   ├── Storyboard Service
   ├── Provider Router
   ├── Generation Coordinator
   ├── Job Scheduler
   ├── Quality Service
   └── Render Service
             │
             ▼
           FFmpeg

Generation Coordinator
   │
   ├── OpenAI Adapter
   ├── Gemini Adapter
   ├── Runway Adapter
   ├── Veo Adapter
   ├── Luma Adapter
   └── Future Adapters

               │
               ▼
            DuckDB
```

Keep services logically separated even if the MVP initially deploys them in one backend process.

---

## 32. Recommended implementation stack

Prefer:

### Frontend

```text
Next.js
React
TypeScript
```

Use a modern component system and build a clean desktop-first editor that remains usable on tablets.

### Backend

```text
ASP.NET Core / C#
```

Use:

- REST API for commands/query
- SSE or WebSockets for generation status
- strongly typed provider clients
- background workers
- dependency injection
- structured logging

### Persistence

```text
DuckDB
```

### Media

```text
FFmpeg / ffprobe
```

Keep provider adapters isolated from core/domain code.

---

## 33. UX principle

The core UX rule is:

> **AI does the first 90%; the user should be able to correct the important 10% without starting over.**

For example, the default screen after analysis should show:

```text
Your video is ready to plan.

Story        ✓
Characters   2
Locations    4
Scenes       27
Duration     3:02
Preset       Balanced
Est. Cost    €XX

[ Review Storyboard ]

[ Generate Video ]
```

Do not confront users initially with:

```text
seed
CFG
provider IDs
model-specific parameters
JSON
```

Those belong in Advanced/Custom settings.

---

## 34. Progressive disclosure

Use three levels:

### Simple

```text
Fast
Balanced
Best Quality
Cheapest
```

### Advanced

User edits:

- shots
- timeline
- character state
- prompts
- scene models

### Expert / Custom

Expose:

- individual model
- seed
- references
- retry thresholds
- provider-specific parameters
- routing
- QA thresholds
- concurrency

---

## 35. Resumability is mandatory

At any moment the application may be closed.

When restarted it must reconstruct:

```text
which project was generating
which scenes completed
which provider tasks still exist
which tasks failed
which are waiting for quota
which dependencies remain
```

and safely continue.

Never use in-memory state as the sole source of truth for generation jobs.

---

## 36. Non-destructive editing

All editing should be non-destructive.

Never overwrite a successful generation when regenerating.

Maintain history:

```text
Scene
 ├── generation 1
 ├── generation 2
 └── generation 3 ★
```

The selected generation is merely a reference.

---

## 37. MVP scope

Implement the first working version in this order:

### Phase 1 — Foundation

- project CRUD
- DuckDB persistence
- media storage
- settings
- provider architecture
- persistent job system

### Phase 2 — Song project

- upload audio
- lyrics
- storyline
- basic audio analysis
- waveform
- editable song sections

### Phase 3 — Libraries

- Character Library
- Style Library
- Location Library
- project references

### Phase 4 — Director

- visual arc
- storyboard generation
- scene editing
- prompt generation

### Phase 5 — Generation

- keyframe generation
- image-to-video
- async jobs
- status
- pause/resume
- retry
- quota handling

### Phase 6 — Assembly

- FFmpeg rendering
- scene ordering
- music synchronization
- preview export

At this point the application must already be genuinely useful.

### Phase 7 — Advanced editor

- waveform/timeline
- clip editing
- regeneration
- transitions
- scene inspector

### Phase 8 — Pro functionality

- QA
- model routing
- cost optimization
- project budgets
- advanced continuity
- character transformation curves
- multiple output formats

---

## 38. Engineering requirements

Maintain:

```text
README.md
PLAN.md
ARCHITECTURE.md
```

`PLAN.md` contains unfinished work only.

Use automated tests for:

- DuckDB repositories
- project persistence
- job state transitions
- pause/resume
- retry behavior
- provider error normalization
- model routing
- prompt versioning
- cost limits
- scene dependency handling

Mock paid AI providers in automated tests.

Never require real paid API calls for normal test execution.

Create integration-test provider adapters such as:

```text
MockImageProvider
MockVideoProvider
MockDirectorProvider
```

that simulate:

- success
- delayed completion
- rate limiting
- quota exhaustion
- rejection
- transient failure
- permanent failure

---

## 39. Critical acceptance scenario

This exact workflow must work:

```text
1. User creates project.

2. Uploads 3:02 MP3.

3. Pastes lyrics.

4. Adds storyline.

5. Selects an existing character.

6. Enables Character Continuity.

7. Selects a style.

8. Chooses Balanced.

9. Application analyzes song.

10. Application produces editable structure.

11. AI Director produces visual arc.

12. Application generates ~25 editable storyboard scenes.

13. User changes Scene 17.

14. Keyframes are generated asynchronously.

15. User approves them.

16. Video generation starts.

17. Provider runs out of credits during Scene 18.

18. Scene 18 becomes WaitingForQuota.

19. Scenes requiring that provider wait safely.

20. Application is closed.

21. User obtains new credits.

22. Application is reopened.

23. Generation state is restored.

24. User selects Retry Now / automatic retry detects availability.

25. Scene 18 completes.

26. Remaining scenes continue.

27. User dislikes Scene 23.

28. Only Scene 23 is regenerated.

29. User chooses the second variant.

30. Application automatically assembles all selected scenes.

31. Original song is synchronized exactly.

32. User opens Advanced Editor.

33. Changes one transition and trims one scene.

34. User exports final 1080p 16:9 MP4.
```

If the architecture cannot cleanly support this scenario, redesign the architecture before adding more features.

---

## 40. Product philosophy

Prioritize:

```text
easy first
power when needed
non-destructive generation
provider independence
character continuity
story continuity
recoverability
cost visibility
scene-level control
```

Avoid:

```text
one-click black boxes
provider lock-in
throwing away successful generations
requiring the user to understand AI APIs
trying to clone a complete video editor
blocking UI during generation
losing state after restart
silent retries that spend unlimited money
```

Build the application incrementally, but design the domain model around the complete workflow from the beginning.

Before implementing major UI complexity, get this loop reliable:

```text
Song
→ Analysis
→ Storyboard
→ Keyframes
→ Animated clips
→ Review/regenerate
→ Final video
```

That loop is the core product.

---

## Additional Product Principle: Character-State Curves

Treat long-running character or environment changes as first-class timeline data, not as isolated prompts.

Examples:

```text
presence
disintegration
confidence
isolation
age
injury
corruption
brightness
weather intensity
environment destruction
```

The AI Director should be able to propose these curves automatically, while the user can manually edit them.

Scene prompts inherit interpolated state values from the timeline.

This turns transformations such as gradual disappearance, aging, emotional progression, changing weather, or environmental decay into a coherent visual arc rather than disconnected scene prompts.
