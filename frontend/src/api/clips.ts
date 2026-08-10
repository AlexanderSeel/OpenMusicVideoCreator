import type { GenerationVariantState } from "./keyframes";

export interface ClipVariantResponse {
  id: string;
  projectId: string;
  sceneId: string;
  variantNumber: number;
  promptVersionId: string;
  startKeyframeVariantId: string;
  endKeyframeVariantId?: string | null;
  jobId?: string | null;
  mediaAssetId?: string | null;
  providerId?: string | null;
  modelId?: string | null;
  state: GenerationVariantState;
  isSelected: boolean;
  durationSeconds: number;
  aspectRatio: string;
  resolution: string;
  estimatedCost?: number | null;
  actualCost?: number | null;
  currency: string;
  createdUtc: string;
  updatedUtc: string;
}

export interface VideoGenerationSettingsRequest {
  providerId?: string | null;
  modelId?: string | null;
  useEndFrame: boolean;
  resolution?: string | null;
  durationSeconds?: number | null;
  allowFallback: boolean;
}

export interface VideoGenerationSettingsResponse extends VideoGenerationSettingsRequest {
  projectId: string;
  sceneId: string;
  updatedUtc: string;
}

export interface ClipGenerationResponse {
  variant: ClipVariantResponse;
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
      // HTTP status remains the fallback when the response body is not JSON.
    }
    throw new Error(`${context} failed with HTTP ${response.status}.${detail}`.trim());
  }
  return (await response.json()) as T;
}

function sceneUrl(projectId: string, sceneId: string): string {
  return `${apiBaseUrl}/api/projects/${projectId}/scenes/${sceneId}/clips`;
}

export async function listClipVariants(projectId: string, sceneId: string, signal?: AbortSignal): Promise<ClipVariantResponse[]> {
  return readJson<ClipVariantResponse[]>(await fetch(`${sceneUrl(projectId, sceneId)}/`, { headers: { Accept: "application/json" }, signal }), "Scene clip variants");
}

export async function getVideoGenerationSettings(projectId: string, sceneId: string, signal?: AbortSignal): Promise<VideoGenerationSettingsResponse> {
  return readJson<VideoGenerationSettingsResponse>(await fetch(`${sceneUrl(projectId, sceneId)}/settings`, { headers: { Accept: "application/json" }, signal }), "Video generation settings");
}

export async function saveVideoGenerationSettings(projectId: string, sceneId: string, request: VideoGenerationSettingsRequest): Promise<VideoGenerationSettingsResponse> {
  return readJson<VideoGenerationSettingsResponse>(await fetch(`${sceneUrl(projectId, sceneId)}/settings`, {
    method: "PUT",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify(request),
  }), "Save video generation settings");
}

export async function generateSceneClip(projectId: string, sceneId: string): Promise<ClipGenerationResponse> {
  return readJson<ClipGenerationResponse>(await fetch(`${sceneUrl(projectId, sceneId)}/generate`, {
    method: "POST",
    headers: { Accept: "application/json" },
  }), "Queue scene clip generation");
}

export async function selectClipVariant(projectId: string, sceneId: string, variantId: string): Promise<ClipVariantResponse> {
  return readJson<ClipVariantResponse>(await fetch(`${sceneUrl(projectId, sceneId)}/${variantId}/select`, {
    method: "POST",
    headers: { Accept: "application/json" },
  }), "Select clip variant");
}

export async function deleteClipVariant(projectId: string, sceneId: string, variantId: string): Promise<void> {
  const response = await fetch(`${sceneUrl(projectId, sceneId)}/${variantId}`, { method: "DELETE" });
  if (!response.ok) await readJson<unknown>(response, "Delete clip variant");
}

export function getClipPreviewUrl(projectId: string, sceneId: string, variantId: string): string {
  return `${sceneUrl(projectId, sceneId)}/${variantId}/preview`;
}
