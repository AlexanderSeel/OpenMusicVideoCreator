import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const studioPath = new URL("../src/features/projects/ProjectStudio.tsx", import.meta.url);
const formPath = new URL("../src/features/projects/ProjectForm.tsx", import.meta.url);
const sidebarPath = new URL("../src/features/projects/ProjectSidebar.tsx", import.meta.url);
const stylesPath = new URL("../app/globals.css", import.meta.url);

test("Simple Mode exposes project intent without provider-specific controls", async () => {
  const [studio, form, sidebar] = await Promise.all([
    readFile(studioPath, "utf8"),
    readFile(formPath, "utf8"),
    readFile(sidebarPath, "utf8"),
  ]);
  const source = `${studio}\n${form}\n${sidebar}`;

  assert.match(source, /Simple/);
  assert.match(source, /Advanced/);
  assert.match(source, /Expert \/ Custom/);
  assert.match(source, /Fast/);
  assert.match(source, /Balanced/);
  assert.match(source, /Best Quality/);
  assert.match(source, /Cheapest/);
  assert.match(source, /Custom/);
  assert.doesNotMatch(source, /providerId/);
  assert.doesNotMatch(source, /modelId/);
  assert.doesNotMatch(source, /seed/i);
  assert.doesNotMatch(source, /negativePrompt/);
});

test("Simple Mode keeps core accessibility and responsive structure", async () => {
  const [studio, sidebar, styles] = await Promise.all([
    readFile(studioPath, "utf8"),
    readFile(sidebarPath, "utf8"),
    readFile(stylesPath, "utf8"),
  ]);

  assert.match(studio, /role="tablist"/);
  assert.match(studio, /aria-selected="true"/);
  assert.match(studio, /aria-live="polite"/);
  assert.match(sidebar, /role="alert"/);
  assert.match(sidebar, /aria-current=/);
  assert.match(styles, /:focus-visible/);
  assert.match(styles, /prefers-reduced-motion/);
  assert.match(styles, /@media \(max-width: 900px\)/);
});
