export type ProjectRenderKind = "Preview" | "Final";
export type ProjectRenderState = "Planned" | "Queued" | "Rendering" | "Completed" | "Failed" | "Cancelled";

export interface RenderTimelineClip {
  sceneId: string;
  sequence: number;
  clipVariantId: string;
  mediaAssetId: string;
  timelineStartSeconds: number;
  durationSeconds: number;
  transitionIn: string;
}

export interface ProjectRenderManifest {
  projectId: string;
  storyboardVersionId: string;
  songMediaAssetId: string;
  kind: ProjectRenderKind;
  width: number;
  height: number;
  framesPerSecond: number;
  clips: RenderTimelineClip[];
  durationSeconds: number;
  timelineSha256: string;
}

export interface ProjectRenderRecord {
  id: string;
  projectId: string;
  version: number;
  manifest: ProjectRenderManifest;
  jobId?: string | null;
  outputMediaAssetId?: string | null;
  state: ProjectRenderState;
  commandLog?: string | null;
  errorMessage?: string | null;
  createdUtc: string;
  updatedUtc: string;
}

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";

async function readJson<T>(response: Response, context: string): Promise<T> {
  if (!response.ok) {
    let detail = "";
    try {
      const body = (await response.json()) as { title?: string; detail?: string; error?: string; errors?: Record<string, string[]> };
      detail = body.errors
        ? ` ${Object.values(body.errors).flat().join(" ")}`
        : body.error
          ? ` ${body.error}`
          : body.detail
            ? ` ${body.detail}`
            : body.title
              ? ` ${body.title}`
              : "";
    } catch {
      // HTTP status remains the fallback when no JSON error body is available.
    }
    throw new Error(`${context} failed with HTTP ${response.status}.${detail}`.trim());
  }
  return (await response.json()) as T;
}

function rendersUrl(projectId: string): string {
  return `${apiBaseUrl}/api/projects/${projectId}/renders`;
}

export async function listProjectRenders(projectId: string, signal?: AbortSignal): Promise<ProjectRenderRecord[]> {
  return readJson<ProjectRenderRecord[]>(await fetch(`${rendersUrl(projectId)}/`, {
    headers: { Accept: "application/json" },
    signal,
  }), "Project renders");
}

export async function queueProjectRender(projectId: string, kind: ProjectRenderKind): Promise<ProjectRenderRecord> {
  return readJson<ProjectRenderRecord>(await fetch(`${rendersUrl(projectId)}/`, {
    method: "POST",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify({ kind }),
  }), `Queue ${kind.toLowerCase()} render`);
}

export function projectRenderOutputUrl(projectId: string, renderId: string): string {
  return `${rendersUrl(projectId)}/${renderId}/output`;
}
