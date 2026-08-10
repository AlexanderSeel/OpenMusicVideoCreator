import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const panelPath = new URL("../src/features/planning/DirectorStoryboardPanel.tsx", import.meta.url);
const sceneReferencesPath = new URL("../src/features/planning/SceneReferenceEditor.tsx", import.meta.url);
const studioPath = new URL("../src/features/projects/ProjectStudio.tsx", import.meta.url);
const clientPath = new URL("../src/api/client.ts", import.meta.url);
const stylesPath = new URL("../app/director.css", import.meta.url);

test("Director workspace exposes all normalized planning controls and editable Visual Arc", async () => {
  const [panel, studio, styles] = await Promise.all([
    readFile(panelPath, "utf8"),
    readFile(studioPath, "utf8"),
    readFile(stylesPath, "utf8"),
  ]);

  assert.match(studio, /DirectorStoryboardPanel/);
  assert.match(panel, /literalToSymbolic/);
  assert.match(panel, /narrativeStrength/);
  assert.match(panel, /abstraction/);
  assert.match(panel, /emotion/);
  assert.match(panel, /darkness/);
  assert.match(panel, /surrealism/);
  assert.match(panel, /complexity/);
  assert.match(panel, /actingIntensity/);
  assert.match(panel, /cameraEnergy/);
  assert.match(panel, /Save Visual Arc/);
  assert.match(panel, /arc\.points\.map/);
  assert.match(styles, /\.visual-arc-editor/);
  assert.match(styles, /\.arc-point/);
});

test("storyboard supports full scene editing reorder references and separate prompt history", async () => {
  const [panel, references, styles] = await Promise.all([
    readFile(panelPath, "utf8"),
    readFile(sceneReferencesPath, "utf8"),
    readFile(stylesPath, "utf8"),
  ]);

  assert.match(panel, /Save scene/);
  assert.match(panel, /Move scene earlier/);
  assert.match(panel, /Move scene later/);
  assert.match(panel, /Director Intent/);
  assert.match(panel, /Scene purpose/);
  assert.match(panel, /Associated lyric/);
  assert.match(panel, /Composition/);
  assert.match(panel, /Lighting/);
  assert.match(panel, /Environment motion/);
  assert.match(panel, /Visual symbolism/);
  assert.match(panel, /Continuity requirements/);
  assert.match(panel, /Final Provider Prompt/);
  assert.match(panel, /Regenerate prompt only/);
  assert.match(panel, /No image\/video generation was started/);
  assert.match(panel, /SceneReferenceEditor/);
  assert.match(references, /characterIds/);
  assert.match(references, /styleIds/);
  assert.match(references, /locationIds/);
  assert.match(styles, /\.scene-reference-editor/);
});

test("typed planning client exposes Director edit and prompt history operations", async () => {
  const client = await readFile(clientPath, "utf8");

  assert.match(client, /export type DirectorControls/);
  assert.match(client, /export type StoryboardSceneResponse/);
  assert.match(client, /export async function getVisualArc/);
  assert.match(client, /export async function getStoryboard/);
  assert.match(client, /export async function planStoryboard/);
  assert.match(client, /export async function saveVisualArc/);
  assert.match(client, /export async function updateStoryboardScene/);
  assert.match(client, /export async function reorderStoryboard/);
  assert.match(client, /export async function listPromptHistory/);
  assert.match(client, /export async function regenerateScenePrompt/);
});

test("prompt-only planning UI does not call job or image/video generation functions", async () => {
  const panel = await readFile(panelPath, "utf8");

  assert.doesNotMatch(panel, /getJobs|createJob|generateImage|generateVideo|providerId|modelId|seed|negativePrompt/);
  assert.match(panel, /regenerateScenePrompt/);
});
