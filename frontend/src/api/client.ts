import type { components, paths } from "./schema";

type SystemVersionResponse = paths["/api/system/version"]["get"]["responses"][200]["content"]["application/json"];
type ProviderCatalogResponse = paths["/api/providers/"]["get"]["responses"][200]["content"]["application/json"];
type JobListResponse = paths["/api/jobs/"]["get"]["responses"][200]["content"]["application/json"];
export type ProjectResponse = components["schemas"]["ProjectResponse"];
export type ProjectUpsertRequest = components["schemas"]["ProjectUpsertRequest"];
export type ProjectSongResponse = components["schemas"]["ProjectSongResponse"];
export type SongAnalysisResponse = components["schemas"]["SongAnalysisResponse"];
export type SongSectionRequest = components["schemas"]["SongSectionRequest"];
export type SongSectionKind = components["schemas"]["SongSectionKind"];

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";

async function readJson<T>(response: Response, context: string): Promise<T> {
  if (!response.ok) {
    let detail = "";
    try {
      const body = (await response.json()) as { title?: string; detail?: string; errors?: Record<string, string[]> };
      detail = body.errors
        ? ` ${Object.values(body.errors).flat().join(" ")}`
        : body.detail
          ? ` ${body.detail}`
          : body.title
            ? ` ${body.title}`
            : "";
    } catch {
      // The status code remains the useful fallback when the response has no JSON problem body.
    }
    throw new Error(`${context} failed with HTTP ${response.status}.${detail}`.trim());
  }

  return (await response.json()) as T;
}

export async function getSystemVersion(signal?: AbortSignal): Promise<SystemVersionResponse> {
  return readJson<SystemVersionResponse>(
    await fetch(`${apiBaseUrl}/api/system/version`, { headers: { Accept: "application/json" }, signal }),
    "Backend version request",
  );
}

export async function getProviderCatalog(signal?: AbortSignal): Promise<ProviderCatalogResponse> {
  return readJson<ProviderCatalogResponse>(
    await fetch(`${apiBaseUrl}/api/providers/`, { headers: { Accept: "application/json" }, signal }),
    "Provider catalog request",
  );
}

export async function getJobs(signal?: AbortSignal): Promise<JobListResponse> {
  return readJson<JobListResponse>(
    await fetch(`${apiBaseUrl}/api/jobs/`, { headers: { Accept: "application/json" }, signal }),
    "Job list request",
  );
}

export async function listProjects(signal?: AbortSignal): Promise<ProjectResponse[]> {
  return readJson<ProjectResponse[]>(
    await fetch(`${apiBaseUrl}/api/projects/`, { headers: { Accept: "application/json" }, signal }),
    "Project list request",
  );
}

export async function createProject(request: ProjectUpsertRequest): Promise<ProjectResponse> {
  return readJson<ProjectResponse>(
    await fetch(`${apiBaseUrl}/api/projects/`, {
      method: "POST",
      headers: { Accept: "application/json", "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }),
    "Create project",
  );
}

export async function updateProject(id: string, request: ProjectUpsertRequest): Promise<ProjectResponse> {
  return readJson<ProjectResponse>(
    await fetch(`${apiBaseUrl}/api/projects/${id}`, {
      method: "PUT",
      headers: { Accept: "application/json", "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }),
    "Update project",
  );
}

export async function deleteProject(id: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/projects/${id}`, { method: "DELETE" });
  if (!response.ok) {
    throw new Error(`Delete project failed with HTTP ${response.status}.`);
  }
}

export async function getProjectSong(id: string, signal?: AbortSignal): Promise<ProjectSongResponse | null> {
  const response = await fetch(`${apiBaseUrl}/api/projects/${id}/song`, {
    headers: { Accept: "application/json" },
    signal,
  });
  if (response.status === 404) {
    return null;
  }
  return readJson<ProjectSongResponse>(response, "Project song request");
}

export async function uploadProjectSong(id: string, file: File): Promise<ProjectSongResponse> {
  const body = new FormData();
  body.append("file", file);
  return readJson<ProjectSongResponse>(
    await fetch(`${apiBaseUrl}/api/projects/${id}/song`, {
      method: "POST",
      headers: { Accept: "application/json" },
      body,
    }),
    "Song upload",
  );
}

export async function getSongAnalysis(
  projectId: string,
  signal?: AbortSignal,
): Promise<SongAnalysisResponse | null> {
  const response = await fetch(`${apiBaseUrl}/api/projects/${projectId}/analysis/`, {
    headers: { Accept: "application/json" },
    signal,
  });
  if (response.status === 404) {
    return null;
  }
  return readJson<SongAnalysisResponse>(response, "Song analysis request");
}

export async function analyzeSong(projectId: string): Promise<SongAnalysisResponse> {
  return readJson<SongAnalysisResponse>(
    await fetch(`${apiBaseUrl}/api/projects/${projectId}/analysis/`, {
      method: "POST",
      headers: { Accept: "application/json" },
    }),
    "Song analysis",
  );
}

export async function listSongAnalysisVersions(
  projectId: string,
  signal?: AbortSignal,
): Promise<SongAnalysisResponse[]> {
  return readJson<SongAnalysisResponse[]>(
    await fetch(`${apiBaseUrl}/api/projects/${projectId}/analysis/versions`, {
      headers: { Accept: "application/json" },
      signal,
    }),
    "Song analysis history",
  );
}

export async function updateSongAnalysisSections(
  projectId: string,
  sections: SongSectionRequest[],
): Promise<SongAnalysisResponse> {
  return readJson<SongAnalysisResponse>(
    await fetch(`${apiBaseUrl}/api/projects/${projectId}/analysis/sections`, {
      method: "PUT",
      headers: { Accept: "application/json", "Content-Type": "application/json" },
      body: JSON.stringify(sections),
    }),
    "Structure Map update",
  );
}
