import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const root = process.cwd();
const read = (relative) => fs.readFileSync(path.join(root, relative), "utf8");

test("Advanced and Custom mount timeline while Simple stays progressively disclosed", () => {
  const studio = read("src/features/projects/ProjectStudio.tsx");
  assert.match(studio, /mode !== "Simple"/);
  assert.match(studio, /AdvancedTimelineAnalysisPanel/);
  assert.match(studio, /AdvancedTimelineEditor/);
});

test("Advanced timeline reuses persisted music analysis lanes", () => {
  const lanes = read("src/features/timeline/TimelineAnalysisLanes.tsx");
  for (const token of ["getSongAnalysis", "getLyricTiming", "analysis.waveform", "analysis.beats", "analysis.bars", "analysis.sections", "analysis.phrases", "Lyrics"]) {
    assert.match(lanes, new RegExp(token.replace(".", "\\.")));
  }
});

test("Advanced Scene Inspector exposes reversible editing groups", () => {
  const editor = read("src/features/timeline/AdvancedTimelineEditor.tsx");
  for (const token of [
    "Original music track protected",
    "Move earlier",
    "Move later",
    "Split at center",
    "Story",
    "Character",
    "Environment",
    "Camera",
    "Generation",
    "Prompt",
    "Source in (s)",
    "Playback rate",
    "Freeze extension (s)",
    "Transition in",
    "Brightness",
    "Contrast",
    "Saturation",
    "Regenerate prompt only",
    "No image/video generation job was started",
    "Save as new timeline version",
  ]) {
    assert.match(editor, new RegExp(token.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
  }
});

test("timeline client covers versioned edits restore overlays and effects", () => {
  const client = read("src/api/timeline.ts");
  for (const token of [
    "initializeTimeline",
    "listTimelineVersions",
    "updateTimelineClip",
    "reorderTimelineClips",
    "replaceTimelineClip",
    "splitTimelineClip",
    "upsertTimelineOverlay",
    "deleteTimelineOverlay",
    "upsertTimelineEffect",
    "deleteTimelineEffect",
    "restoreTimelineVersion",
  ]) {
    assert.match(client, new RegExp(token));
  }
});

test("render client pins Advanced timeline provenance", () => {
  const renders = read("src/api/renders.ts");
  assert.match(renders, /timelineVersionId/);
  assert.match(renders, /overlays/);
  assert.match(renders, /effects/);
  assert.match(renders, /sourceInSeconds/);
  assert.match(renders, /playbackRate/);
});
