import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const panelPath = new URL("../src/features/analysis/SongAnalysisPanel.tsx", import.meta.url);
const studioPath = new URL("../src/features/projects/ProjectStudio.tsx", import.meta.url);
const stylesPath = new URL("../app/globals.css", import.meta.url);

test("song analysis UI exposes waveform, rhythm markers, vocal estimate, lyrics timing, and editable Structure Map", async () => {
  const [panel, studio, styles] = await Promise.all([
    readFile(panelPath, "utf8"),
    readFile(studioPath, "utf8"),
    readFile(stylesPath, "utf8"),
  ]);

  assert.match(studio, /SongAnalysisPanel/);
  assert.match(panel, /Waveform, beats, bars & phrases/);
  assert.match(panel, /Quiet ranges/);
  assert.match(panel, /analysis\.bars/);
  assert.match(panel, /analysis\.phrases/);
  assert.match(panel, /analysis\.quietRanges/);
  assert.match(panel, /Likely vocal/);
  assert.match(panel, /Likely instrumental/);
  assert.match(panel, /analysis\.vocalActivity/);
  assert.match(panel, /Lyrics lane · authoritative text/);
  assert.match(panel, /getLyricTiming/);
  assert.match(panel, /Transcription only suggests timestamps/);
  assert.match(panel, /Structure Map/);
  assert.match(panel, /Save Structure Map/);
  assert.match(panel, /start in seconds/);
  assert.match(panel, /end in seconds/);
  assert.match(panel, /Analyze song/);
  assert.match(panel, /Analysis version/);
  assert.match(styles, /\.waveform-sample/);
  assert.match(styles, /\.beat-marker/);
  assert.match(styles, /\.section-row/);
});

test("song analysis UI remains provider independent", async () => {
  const panel = await readFile(panelPath, "utf8");

  assert.doesNotMatch(panel, /providerId/);
  assert.doesNotMatch(panel, /modelId/);
  assert.doesNotMatch(panel, /seed/i);
  assert.doesNotMatch(panel, /negativePrompt/);
});
