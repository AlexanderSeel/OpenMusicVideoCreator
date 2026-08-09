import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const studioPath = new URL("../src/features/projects/ProjectStudio.tsx", import.meta.url);
const stylesPath = new URL("../app/globals.css", import.meta.url);

test("Simple Mode exposes project intent without provider-specific controls", async () => {
  const source = await readFile(studioPath, "utf8");

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
  const [source, styles] = await Promise.all([
    readFile(studioPath, "utf8"),
    readFile(stylesPath, "utf8"),
  ]);

  assert.match(source, /role="tablist"/);
  assert.match(source, /aria-selected="true"/);
  assert.match(source, /aria-live="polite"/);
  assert.match(source, /role="alert"/);
  assert.match(source, /aria-current=/);
  assert.match(styles, /:focus-visible/);
  assert.match(styles, /prefers-reduced-motion/);
  assert.match(styles, /@media \(max-width: 900px\)/);
});
