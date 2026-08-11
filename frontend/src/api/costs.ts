import type { JobState } from "./jobs";

export interface CostBreakdown {
  providerId?: string | null;
  modelId?: string | null;
  actualCost: number;
  reservedEstimatedCost: number;
  jobCount: number;
}

export interface SceneCostBreakdown {
  sceneId?: string | null;
  actualCost: number;
  reservedEstimatedCost: number;
  jobCount: number;
}

export interface GenerationCostBreakdown {
  jobId: string;
  sceneId?: string | null;
  type: string;
  providerId?: string | null;
  modelId?: string | null;
  state: JobState;
  actualCost: number;
  reservedEstimatedCost: number;
  createdUtc: string;
}

export interface ProjectCostSummary {
  projectId: string;
  currency: string;
  estimatedBudget?: number | null;
  maximumBudget?: number | null;
  actualCost: number;
  reservedEstimatedCost: number;
  projectedCost: number;
  remainingBudget?: number | null;
  unknownCostJobCount: number;
  generations: GenerationCostBreakdown[];
  providers: CostBreakdown[];
  scenes: SceneCostBreakdown[];
}

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";

export async function getProjectCosts(projectId: string, signal?: AbortSignal): Promise<ProjectCostSummary> {
  const response = await fetch(`${apiBaseUrl}/api/projects/${projectId}/costs`, {
    headers: { Accept: "application/json" },
    signal,
  });
  if (!response.ok) {
    throw new Error(`Project cost summary failed with HTTP ${response.status}.`);
  }
  return (await response.json()) as ProjectCostSummary;
}
