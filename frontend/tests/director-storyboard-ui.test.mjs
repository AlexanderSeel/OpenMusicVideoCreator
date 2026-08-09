import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const panelPath = new URL("../src/features/planning/DirectorStoryboardPanel.tsx", import.meta.url);
const sceneReferencesPath = new URL("../src/features/planning/SceneReferenceEditor.tsx", import.meta.url);
const studioPath = new URL("../src/features/projects/ProjectStudio.tsx", import.meta.url);
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

test("storyboard supports scene editing reorder references and separate prompt history", async () => {
  const [panel, references] = await Promise.all([
    readFile(panelPath, "utf8"),
    readFile(sceneReferencesPath, "utf8"),
  ]);

  assert.match(panel, /Save scene/);
  assert.match(panel, /Move scene earlier/);
  assert.match(panel, /Move scene later/);
  assert.match(panel, /Director Intent/);
  assert.match(panel, /Final Provider Prompt/);
  assert.match(panel, /Regenerate prompt only/);
  assert.match(panel, /No image\/video generation was started/);
  assert.match(references, /characterIds/);
  assert.match(references, /styleIds/);
  assert.match(references, /locationIds/);
});

test("prompt-only planning UI does not call job or image/video generation functions", async () => {
  const panel = await readFile(panelPath, "utf8");

  assert.doesNotMatch(panel, /getJobs|createJob|generateImage|generateVideo|providerId|modelId|seed|negativePrompt/);
  assert.match(panel, /regenerateScenePrompt/);
});
