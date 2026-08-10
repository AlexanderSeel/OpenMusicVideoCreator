import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const root = process.cwd();
const read = (relative) => fs.readFileSync(path.join(root, relative), "utf8");

test("project studio mounts deterministic render workspace", () => {
  const studio = read("src/features/projects/ProjectStudio.tsx");
  assert.match(studio, /ProjectRenderWorkspace/);
  assert.match(studio, /<ProjectRenderWorkspace projectId=\{editor\.id\}/);
});

test("render workspace exposes preview final provenance lifecycle and download flow", () => {
  const workspace = read("src/features/rendering/ProjectRenderWorkspace.tsx");
  for (const token of [
    "Render preview",
    "Render final MP4",
    "timelineSha256",
    "storyboardVersionId",
    "songMediaAssetId",
    "projectRenderOutputUrl",
    "Cancel render",
    "Retry same render",
    "Render attempts",
    "Deterministic FFmpeg command",
  ]) {
    assert.match(workspace, new RegExp(token));
  }
});

test("render client covers queue cancel retry attempts and output", () => {
  const client = read("src/api/renders.ts");
  for (const token of [
    "queueProjectRender",
    "cancelProjectRender",
    "retryProjectRender",
    "ProjectRenderAttempt",
    "/cancel",
    "/retry",
    "/output",
  ]) {
    assert.match(client, new RegExp(token));
  }
});
