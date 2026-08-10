import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const workspacePath = new URL("../src/features/generation/KeyframeWorkspace.tsx", import.meta.url);
const apiPath = new URL("../src/api/keyframes.ts", import.meta.url);
const studioPath = new URL("../src/features/projects/ProjectStudio.tsx", import.meta.url);
const layoutPath = new URL("../app/layout.tsx", import.meta.url);
const stylesPath = new URL("../app/keyframes.css", import.meta.url);

test("project studio exposes the implemented Director to keyframe workflow", async () => {
  const [studio, layout] = await Promise.all([readFile(studioPath, "utf8"), readFile(layoutPath, "utf8")]);

  assert.match(studio, /DirectorStoryboardPanel/);
  assert.match(studio, /KeyframeWorkspace/);
  assert.match(studio, /<DirectorStoryboardPanel[\s\S]*<KeyframeWorkspace/);
  assert.match(layout, /director\.css/);
  assert.match(layout, /keyframes\.css/);
});

test("keyframe workspace provides async variants advanced settings and approval", async () => {
  const [workspace, styles] = await Promise.all([readFile(workspacePath, "utf8"), readFile(stylesPath, "utf8")]);

  assert.match(workspace, /Generate start keyframe/);
  assert.match(workspace, /Generate start \+ end/);
  assert.match(workspace, /Regenerate start/);
  assert.match(workspace, /Generate \/ regenerate end/);
  assert.match(workspace, /Advanced \/ Custom generation settings/);
  assert.match(workspace, /Automatic capability routing/);
  assert.match(workspace, /supportsReferences/);
  assert.match(workspace, /supportsSeed/);
  assert.match(workspace, /supportsNegativePrompt/);
  assert.match(workspace, /Select/);
  assert.match(workspace, /Delete/);
  assert.match(workspace, /Approve for animation/);
  assert.match(workspace, /Revoke approval/);
  assert.match(styles, /\.keyframe-variant\.is-selected/);
  assert.match(styles, /\.keyframe-approval\.is-approved/);
});

test("typed keyframe client uses dedicated persisted generation routes", async () => {
  const api = await readFile(apiPath, "utf8");

  assert.match(api, /KeyframeVariantResponse/);
  assert.match(api, /KeyframeGenerationSettingsResponse/);
  assert.match(api, /listKeyframeVariants/);
  assert.match(api, /getKeyframeSettings/);
  assert.match(api, /saveKeyframeSettings/);
  assert.match(api, /generateKeyframes/);
  assert.match(api, /selectKeyframeVariant/);
  assert.match(api, /deleteKeyframeVariant/);
  assert.match(api, /getKeyframePreviewUrl/);
  assert.match(api, /approveKeyframes/);
  assert.match(api, /revokeKeyframeApproval/);
  assert.doesNotMatch(api, /generateImage|mock:\/\/image/);
});

test("keyframe UI polls scene variants only while generation is active", async () => {
  const workspace = await readFile(workspacePath, "utf8");

  assert.match(workspace, /activeStates/);
  assert.match(workspace, /hasActiveVariants/);
  assert.match(workspace, /1500/);
  assert.doesNotMatch(workspace, /getJobs\(/);
});
