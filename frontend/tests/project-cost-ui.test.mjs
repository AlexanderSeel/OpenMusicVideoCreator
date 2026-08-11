import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const root = process.cwd();
const read = (relative) => fs.readFileSync(path.join(root, relative), "utf8");

test("project studio mounts live cost workspace and refreshes after saves", () => {
  const studio = read("src/features/projects/ProjectStudio.tsx");
  assert.match(studio, /ProjectCostPanel/);
  assert.match(studio, /refreshKey=\{costRefreshKey\}/);
  assert.match(studio, /setCostRefreshKey\(\(current\) => current \+ 1\)/);
});

test("cost panel shows simple totals and progressively disclosed detail", () => {
  const panel = read("src/features/costs/ProjectCostPanel.tsx");
  for (const token of [
    "Actual spend",
    "Reserved",
    "Projected",
    "Remaining hard cap",
    "Projected budget utilization",
    "unknown cost",
    "mode !== \"Simple\"",
    "Provider / model",
    "Scene",
    "Generation cost history",
    "jobEventsUrl",
    "EventSource",
  ]) {
    assert.match(panel, new RegExp(token.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
  }
});

test("typed cost client exposes generation scene provider and budget accounting", () => {
  const client = read("src/api/costs.ts");
  for (const token of [
    "ProjectCostSummary",
    "GenerationCostBreakdown",
    "SceneCostBreakdown",
    "CostBreakdown",
    "actualCost",
    "reservedEstimatedCost",
    "projectedCost",
    "remainingBudget",
    "unknownCostJobCount",
    "/costs",
  ]) {
    assert.match(client, new RegExp(token));
  }
});

test("budget enforcement is wired at shared persisted enqueue boundary", () => {
  const program = read("../backend/src/OpenMusicVideoCreator.Api/Program.cs");
  const queue = read("../backend/src/OpenMusicVideoCreator.Application/Costs/BudgetAwareJobQueue.cs");
  const keyframes = read("../backend/src/OpenMusicVideoCreator.Application/Generation/KeyframeGenerationCoordinator.cs");
  const videos = read("../backend/src/OpenMusicVideoCreator.Application/Generation/VideoGenerationCoordinator.cs");
  assert.match(program, /IJobQueue.*BudgetAwareJobQueue/);
  assert.match(queue, /ExecuteWithinBudgetAsync/);
  assert.match(keyframes, /EnsureCanReserveAsync/);
  assert.match(videos, /EnsureCanReserveAsync/);
});
