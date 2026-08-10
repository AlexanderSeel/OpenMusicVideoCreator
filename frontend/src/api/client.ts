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
export type TranscriptionSegmentRequest = components["schemas"]["TranscriptionSegmentRequest"];
export type LyricTimingResponse = components["schemas"]["LyricTimingResponse"];
export type VisualLibraryKind = components["schemas"]["VisualLibraryKind"];
export type VisualLibraryResponse = components["schemas"]["VisualLibraryResponse"];
export type VisualLibraryUpsertRequest = components["schemas"]["VisualLibraryUpsertRequest"];
export type AssetLibraryResponse = components["schemas"]["AssetLibraryResponse"];
export type AssetLibraryUpdateRequest = components["schemas"]["AssetLibraryUpdateRequest"];
export type ProjectCharacterStateRequest = components["schemas"]["ProjectCharacterStateRequest"];
export type ProjectCharacterStateResponse = components["schemas"]["ProjectCharacterStateResponse"];
export type DirectorControls = components["schemas"]["DirectorControls"];
export type DirectorPlanResponse = components["schemas"]["DirectorPlanResponse"];
export type VisualArcResponse = components["schemas"]["VisualArcResponse"];
export type VisualArcUpdateRequest = components["schemas"]["VisualArcUpdateRequest"];
export type StoryboardSceneDetailsRequest = components["schemas"]["StoryboardSceneDetailsRequest"];
export type StoryboardResponse = components["schemas"]["StoryboardResponse"];
export type StoryboardSceneResponse = components["schemas"]["StoryboardSceneResponse"];
export type SceneUpdateRequest = components["schemas"]["SceneUpdateRequest"];
export type PromptVersionResponse = components["schemas"]["PromptVersionResponse"];
export type PromptRegenerateResponse = components["schemas"]["PromptRegenerateResponse"];

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
  return readJson<SystemVersionResponse>(await fetch(`${apiBaseUrl}/api/system/version`, { headers: { Accept: "application/json" }, signal }), "Backend version request");
}

export async function getProviderCatalog(signal?: AbortSignal): Promise<ProviderCatalogResponse> {
  return readJson<ProviderCatalogResponse>(await fetch(`${apiBaseUrl}/api/providers/`, { headers: { Accept: "application/json" }, signal }), "Provider catalog request");
}

export async function getJobs(signal?: AbortSignal): Promise<JobListResponse> {
  return readJson<JobListResponse>(await fetch(`${apiBaseUrl}/api/jobs/`, { headers: { Accept: "application/json" }, signal }), "Job list request");
}

export async function listProjects(signal?: AbortSignal): Promise<ProjectResponse[]> {
  return readJson<ProjectResponse[]>(await fetch(`${apiBaseUrl}/api/projects/`, { headers: { Accept: "application/json" }, signal }), "Project list request");
}

export async function createProject(request: ProjectUpsertRequest): Promise<ProjectResponse> {
  return readJson<ProjectResponse>(await fetch(`${apiBaseUrl}/api/projects/`, { method: "POST", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify(request) }), "Create project");
}

export async function updateProject(id: string, request: ProjectUpsertRequest): Promise<ProjectResponse> {
  return readJson<ProjectResponse>(await fetch(`${apiBaseUrl}/api/projects/${id}`, { method: "PUT", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify(request) }), "Update project");
}

export async function deleteProject(id: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/projects/${id}`, { method: "DELETE" });
  if (!response.ok) throw new Error(`Delete project failed with HTTP ${response.status}.`);
}

export async function getProjectSong(id: string, signal?: AbortSignal): Promise<ProjectSongResponse | null> {
  const response = await fetch(`${apiBaseUrl}/api/projects/${id}/song`, { headers: { Accept: "application/json" }, signal });
  if (response.status === 404) return null;
  return readJson<ProjectSongResponse>(response, "Project song request");
}

export async function uploadProjectSong(id: string, file: File): Promise<ProjectSongResponse> {
  const body = new FormData();
  body.append("file", file);
  return readJson<ProjectSongResponse>(await fetch(`${apiBaseUrl}/api/projects/${id}/song`, { method: "POST", headers: { Accept: "application/json" }, body }), "Song upload");
}

export async function getSongAnalysis(projectId: string, signal?: AbortSignal): Promise<SongAnalysisResponse | null> {
  const response = await fetch(`${apiBaseUrl}/api/projects/${projectId}/analysis/`, { headers: { Accept: "application/json" }, signal });
  if (response.status === 404) return null;
  return readJson<SongAnalysisResponse>(response, "Song analysis request");
}

export async function analyzeSong(projectId: string): Promise<SongAnalysisResponse> {
  return readJson<SongAnalysisResponse>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/analysis/`, { method: "POST", headers: { Accept: "application/json" } }), "Song analysis");
}

export async function listSongAnalysisVersions(projectId: string, signal?: AbortSignal): Promise<SongAnalysisResponse[]> {
  return readJson<SongAnalysisResponse[]>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/analysis/versions`, { headers: { Accept: "application/json" }, signal }), "Song analysis history");
}

export async function updateSongAnalysisSections(projectId: string, sections: SongSectionRequest[]): Promise<SongAnalysisResponse> {
  return readJson<SongAnalysisResponse>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/analysis/sections`, { method: "PUT", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify(sections) }), "Structure Map update");
}

export async function getLyricTiming(projectId: string, signal?: AbortSignal): Promise<LyricTimingResponse | null> {
  const response = await fetch(`${apiBaseUrl}/api/projects/${projectId}/analysis/lyrics/timing`, { headers: { Accept: "application/json" }, signal });
  if (response.status === 404) return null;
  return readJson<LyricTimingResponse>(response, "Lyric timing request");
}

export async function applyTranscriptionLyricTiming(projectId: string, segments: TranscriptionSegmentRequest[]): Promise<LyricTimingResponse> {
  return readJson<LyricTimingResponse>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/analysis/lyrics/timing`, { method: "POST", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify(segments) }), "Lyric timing alignment");
}

export async function listLyricTimingVersions(projectId: string, signal?: AbortSignal): Promise<LyricTimingResponse[]> {
  return readJson<LyricTimingResponse[]>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/analysis/lyrics/timing/versions`, { headers: { Accept: "application/json" }, signal }), "Lyric timing history");
}

export async function getVisualArc(projectId: string, signal?: AbortSignal): Promise<VisualArcResponse | null> {
  const response = await fetch(`${apiBaseUrl}/api/projects/${projectId}/director/visual-arc`, { headers: { Accept: "application/json" }, signal });
  if (response.status === 404) return null;
  return readJson<VisualArcResponse>(response, "Visual Arc request");
}

export async function listVisualArcVersions(projectId: string, signal?: AbortSignal): Promise<VisualArcResponse[]> {
  return readJson<VisualArcResponse[]>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/director/visual-arc/versions`, { headers: { Accept: "application/json" }, signal }), "Visual Arc history");
}

export async function getStoryboard(projectId: string, signal?: AbortSignal): Promise<StoryboardResponse | null> {
  const response = await fetch(`${apiBaseUrl}/api/projects/${projectId}/director/storyboard`, { headers: { Accept: "application/json" }, signal });
  if (response.status === 404) return null;
  return readJson<StoryboardResponse>(response, "Storyboard request");
}

export async function listStoryboardVersions(projectId: string, signal?: AbortSignal): Promise<StoryboardResponse[]> {
  return readJson<StoryboardResponse[]>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/director/storyboard/versions`, { headers: { Accept: "application/json" }, signal }), "Storyboard history");
}

export async function planStoryboard(projectId: string, controls: DirectorControls): Promise<DirectorPlanResponse> {
  return readJson<DirectorPlanResponse>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/director/plan`, { method: "POST", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify({ controls }) }), "Director planning");
}

export async function saveVisualArc(projectId: string, request: VisualArcUpdateRequest): Promise<VisualArcResponse> {
  return readJson<VisualArcResponse>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/director/visual-arc`, { method: "PUT", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify(request) }), "Save Visual Arc");
}

export async function updateStoryboardScene(projectId: string, sceneId: string, request: SceneUpdateRequest): Promise<StoryboardResponse> {
  return readJson<StoryboardResponse>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/director/storyboard/scenes/${sceneId}`, { method: "PUT", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify(request) }), "Save storyboard scene");
}

export async function reorderStoryboard(projectId: string, sceneIds: string[]): Promise<StoryboardResponse> {
  return readJson<StoryboardResponse>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/director/storyboard/reorder`, { method: "POST", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify({ sceneIds }) }), "Reorder storyboard");
}

export async function listPromptHistory(projectId: string, sceneId: string, signal?: AbortSignal): Promise<PromptVersionResponse[]> {
  return readJson<PromptVersionResponse[]>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/director/storyboard/scenes/${sceneId}/prompts`, { headers: { Accept: "application/json" }, signal }), "Prompt history");
}

export async function regenerateScenePrompt(projectId: string, sceneId: string, notes?: string): Promise<PromptRegenerateResponse> {
  return readJson<PromptRegenerateResponse>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/director/storyboard/scenes/${sceneId}/prompts/regenerate`, { method: "POST", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify({ notes: notes?.trim() || null }) }), "Regenerate scene prompt");
}

export async function listVisualLibrary(kind?: VisualLibraryKind, signal?: AbortSignal): Promise<VisualLibraryResponse[]> {
  const search = new URLSearchParams();
  if (kind) search.set("kind", kind);
  const suffix = search.size ? `?${search.toString()}` : "";
  return readJson<VisualLibraryResponse[]>(await fetch(`${apiBaseUrl}/api/library/items${suffix}`, { headers: { Accept: "application/json" }, signal }), "Visual library request");
}

export async function createVisualLibraryItem(request: VisualLibraryUpsertRequest): Promise<VisualLibraryResponse> {
  return readJson<VisualLibraryResponse>(await fetch(`${apiBaseUrl}/api/library/items`, { method: "POST", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify(request) }), "Create visual library item");
}

export async function updateVisualLibraryItem(id: string, request: VisualLibraryUpsertRequest): Promise<VisualLibraryResponse> {
  return readJson<VisualLibraryResponse>(await fetch(`${apiBaseUrl}/api/library/items/${id}`, { method: "PUT", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify(request) }), "Update visual library item");
}

export async function deleteVisualLibraryItem(id: string): Promise<{ deleted: boolean; referencingIds: string[] }> {
  const response = await fetch(`${apiBaseUrl}/api/library/items/${id}`, { method: "DELETE", headers: { Accept: "application/json" } });
  if (response.status === 409) return (await response.json()) as { deleted: boolean; referencingIds: string[] };
  return readJson<{ deleted: boolean; referencingIds: string[] }>(response, "Delete visual library item");
}

export async function listAssetLibrary(signal?: AbortSignal): Promise<AssetLibraryResponse[]> {
  return readJson<AssetLibraryResponse[]>(await fetch(`${apiBaseUrl}/api/library/assets`, { headers: { Accept: "application/json" }, signal }), "Asset library request");
}

export async function uploadAssetLibrary(file: File, name?: string, tags?: string[], sourceDescription?: string): Promise<AssetLibraryResponse> {
  const body = new FormData();
  body.append("file", file);
  if (name) body.append("name", name);
  if (tags?.length) body.append("tags", tags.join(","));
  if (sourceDescription) body.append("sourceDescription", sourceDescription);
  return readJson<AssetLibraryResponse>(await fetch(`${apiBaseUrl}/api/library/assets`, { method: "POST", headers: { Accept: "application/json" }, body }), "Upload library asset");
}

export async function updateAssetLibrary(id: string, request: AssetLibraryUpdateRequest): Promise<AssetLibraryResponse> {
  return readJson<AssetLibraryResponse>(await fetch(`${apiBaseUrl}/api/library/assets/${id}`, { method: "PUT", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify(request) }), "Update library asset");
}

export async function deleteAssetLibrary(id: string): Promise<{ deleted: boolean; referencingIds: string[] }> {
  const response = await fetch(`${apiBaseUrl}/api/library/assets/${id}`, { method: "DELETE", headers: { Accept: "application/json" } });
  if (response.status === 409) return (await response.json()) as { deleted: boolean; referencingIds: string[] };
  return readJson<{ deleted: boolean; referencingIds: string[] }>(response, "Delete library asset");
}

export function getAssetPreviewUrl(id: string): string {
  return `${apiBaseUrl}/api/library/assets/${id}/preview`;
}

export async function listProjectCharacterStates(projectId: string, signal?: AbortSignal): Promise<ProjectCharacterStateResponse[]> {
  return readJson<ProjectCharacterStateResponse[]>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/characters/states/`, { headers: { Accept: "application/json" }, signal }), "Project character states");
}

export async function saveProjectCharacterState(projectId: string, characterId: string, request: ProjectCharacterStateRequest): Promise<ProjectCharacterStateResponse> {
  return readJson<ProjectCharacterStateResponse>(await fetch(`${apiBaseUrl}/api/projects/${projectId}/characters/states/${characterId}`, { method: "PUT", headers: { Accept: "application/json", "Content-Type": "application/json" }, body: JSON.stringify(request) }), "Save project character state");
}
