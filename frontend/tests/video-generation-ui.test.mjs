import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const studioPath = new URL("../src/features/projects/ProjectStudio.tsx", import.meta.url);
const videoPath = new URL("../src/features/generation/VideoGenerationWorkspace.tsx", import.meta.url);
const queuePath = new URL("../src/features/generation/GenerationQueuePanel.tsx", import.meta.url);
const clipsApiPath = new URL("../src/api/clips.ts", import.meta.url);
const jobsApiPath = new URL("../src/api/jobs.ts", import.meta.url);
const layoutPath = new URL("../app/layout.tsx", import.meta.url);
const stylesPath = new URL("../app/generation.css", import.meta.url);

test("project studio mounts keyframes then animation then persistent queue", async () => {
  const [studio, layout] = await Promise.all([readFile(studioPath, "utf8"), readFile(layoutPath, "utf8")]);
  assert.match(studio, /KeyframeWorkspace/);
  assert.match(studio, /VideoGenerationWorkspace/);
  assert.match(studio, /GenerationQueuePanel/);
  assert.match(studio, /<KeyframeWorkspace[\s\S]*<VideoGenerationWorkspace[\s\S]*<GenerationQueuePanel/);
  assert.match(layout, /generation\.css/);
});

test("video workspace enforces approval and keeps non-destructive variants", async () => {
  const [workspace, styles] = await Promise.all([readFile(videoPath, "utf8"), readFile(stylesPath, "utf8")]);
  assert.match(workspace, /Keyframe approval required/);
  assert.match(workspace, /Animate approved keyframes/);
  assert.match(workspace, /Regenerate scene/);
  assert.match(workspace, /Automatic capability routing/);
  assert.match(workspace, /supportsEndFrame/);
  assert.match(workspace, /Allow automatic provider fallback/);
  assert.match(workspace, /selectClipVariant/);
  assert.match(workspace, /deleteClipVariant/);
  assert.match(workspace, /<video controls muted/);
  assert.match(styles, /\.clip-card\.is-selected/);
});

test("clip API uses persisted scene generation routes", async () => {
  const api = await readFile(clipsApiPath, "utf8");
  assert.match(api, /ClipVariantResponse/);
  assert.match(api, /VideoGenerationSettingsResponse/);
  assert.match(api, /listClipVariants/);
  assert.match(api, /generateSceneClip/);
  assert.match(api, /selectClipVariant/);
  assert.match(api, /deleteClipVariant/);
  assert.match(api, /getClipPreviewUrl/);
  assert.doesNotMatch(api, /mock:\/\/video/);
});

test("generation queue consumes SSE rather than polling every scene", async () => {
  const [queue, api] = await Promise.all([readFile(queuePath, "utf8"), readFile(jobsApiPath, "utf8")]);
  assert.match(queue, /new EventSource\(jobEventsUrl\(\)\)/);
  assert.match(queue, /addEventListener\("job"/);
  assert.match(queue, /Pause project/);
  assert.match(queue, /Resume project/);
  assert.match(queue, /Retry/);
  assert.match(queue, /Restart/);
  assert.match(queue, /Cancel/);
  assert.match(api, /\/api\/jobs\/events/);
  assert.doesNotMatch(queue, /setInterval\(.*listJobs/s);
});
