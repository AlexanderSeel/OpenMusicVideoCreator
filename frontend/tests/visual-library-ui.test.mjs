import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const studioPath = new URL("../src/features/projects/ProjectStudio.tsx", import.meta.url);
const formPath = new URL("../src/features/projects/ProjectForm.tsx", import.meta.url);
const panelPath = new URL("../src/features/library/VisualLibraryPanel.tsx", import.meta.url);
const selectorPath = new URL("../src/features/library/VisualReferenceSelector.tsx", import.meta.url);
const continuityPath = new URL("../src/features/library/ProjectCharacterContinuity.tsx", import.meta.url);
const stylesPath = new URL("../app/library.css", import.meta.url);

test("visual library is reusable across project editing instead of copied into projects", async () => {
  const [studio, form, selector] = await Promise.all([
    readFile(studioPath, "utf8"),
    readFile(formPath, "utf8"),
    readFile(selectorPath, "utf8"),
  ]);

  assert.match(studio, /listVisualLibrary/);
  assert.match(studio, /VisualLibraryPanel/);
  assert.match(form, /VisualReferenceSelector/);
  assert.match(form, /kind="Character"/);
  assert.match(form, /kind="Style"/);
  assert.match(form, /kind="Location"/);
  assert.match(selector, /referenceId/);
  assert.doesNotMatch(selector, /appearanceDescription:/);
  assert.doesNotMatch(selector, /cameraCharacteristics:/);
});

test("library workspace exposes search favorites previews source tracking and safe deletion UI", async () => {
  const [panel, styles] = await Promise.all([
    readFile(panelPath, "utf8"),
    readFile(stylesPath, "utf8"),
  ]);

  assert.match(panel, /Visual Library/);
  assert.match(panel, /Search/);
  assert.match(panel, /Favorite/);
  assert.match(panel, /Reference asset/);
  assert.match(panel, /getAssetPreviewUrl/);
  assert.match(panel, /sourceDescription/);
  assert.match(panel, /referencingIds/);
  assert.match(panel, /Character/);
  assert.match(panel, /Style/);
  assert.match(panel, /Location/);
  assert.match(styles, /\.library-card/);
  assert.match(styles, /\.asset-preview/);
  assert.match(styles, /\.reference-selector/);
});

test("project character continuity exposes locks outfits and normalized state seeds", async () => {
  const continuity = await readFile(continuityPath, "utf8");

  assert.match(continuity, /Character continuity/);
  assert.match(continuity, /outfits/);
  assert.match(continuity, /identity/);
  assert.match(continuity, /presence/);
  assert.match(continuity, /confidence/);
  assert.match(continuity, /isolation/);
  assert.match(continuity, /saveProjectCharacterState/);
});
