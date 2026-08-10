export type KeyframeRole = "Start" | "End";
export type GenerationVariantState = "Planned" | "Queued" | "Generating" | "Completed" | "Failed" | "Cancelled";

export interface KeyframeVariantResponse {
  id: string;
  projectId: string;
  sceneId: string;
  role: KeyframeRole;
  variantNumber: number;
  promptVersionId: string;
  jobId?: string | null;
  mediaAssetId?: string | null;
  providerId?: string | null;
  modelId?: string | null;
  state: GenerationVariantState;
  isSelected: boolean;
  estimatedCost?: number | null;
  actualCost?: number | null;
  currency: string;
  createdUtc: string;
  updatedUtc: string;
}

export interface KeyframeGenerationSettingsRequest {
  providerId?: string | null;
  modelId?: string | null;
  generateEndFrame: boolean;
  resolution?: string | null;
  seed?: number | null;
  negativePrompt?: string | null;
}

export interface KeyframeGenerationSettingsResponse extends KeyframeGenerationSettingsRequest {
  projectId: string;
  sceneId: string;
  updatedUtc: string;
}

export interface KeyframeGenerationResponse {
  variants: KeyframeVariantResponse[];
}

export interface KeyframeApprovalStatusResponse {
  isApproved: boolean;
  startVariantId?: string | null;
  endVariantId?: string | null;
  approvedUtc?: string | null;
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
  return `${apiBaseUrl}/api/projects/${projectId}/scenes/${sceneId}/keyframes`;
}

export async function listKeyframeVariants(projectId: string, sceneId: string, signal?: AbortSignal): Promise<KeyframeVariantResponse[]> {
  return readJson<KeyframeVariantResponse[]>(await fetch(`${sceneUrl(projectId, sceneId)}/`, { headers: { Accept: "application/json" }, signal }), "Keyframe variants");
}

export async function getKeyframeSettings(projectId: string, sceneId: string, signal?: AbortSignal): Promise<KeyframeGenerationSettingsResponse> {
  return readJson<KeyframeGenerationSettingsResponse>(await fetch(`${sceneUrl(projectId, sceneId)}/settings`, { headers: { Accept: "application/json" }, signal }), "Keyframe settings");
}

export async function saveKeyframeSettings(projectId: string, sceneId: string, request: KeyframeGenerationSettingsRequest): Promise<KeyframeGenerationSettingsResponse> {
  return readJson<KeyframeGenerationSettingsResponse>(await fetch(`${sceneUrl(projectId, sceneId)}/settings`, {
    method: "PUT",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify(request),
  }), "Save keyframe settings");
}

export async function generateKeyframes(projectId: string, sceneId: string, role?: KeyframeRole): Promise<KeyframeGenerationResponse> {
  return readJson<KeyframeGenerationResponse>(await fetch(`${sceneUrl(projectId, sceneId)}/generate`, {
    method: "POST",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify({ role: role ?? null }),
  }), "Queue keyframe generation");
}

export async function selectKeyframeVariant(projectId: string, sceneId: string, variantId: string): Promise<KeyframeVariantResponse> {
  return readJson<KeyframeVariantResponse>(await fetch(`${sceneUrl(projectId, sceneId)}/${variantId}/select`, {
    method: "POST",
    headers: { Accept: "application/json" },
  }), "Select keyframe variant");
}

export async function deleteKeyframeVariant(projectId: string, sceneId: string, variantId: string): Promise<void> {
  const response = await fetch(`${sceneUrl(projectId, sceneId)}/${variantId}`, { method: "DELETE" });
  if (!response.ok) {
    await readJson<unknown>(response, "Delete keyframe variant");
  }
}

export function getKeyframePreviewUrl(projectId: string, sceneId: string, variantId: string): string {
  return `${sceneUrl(projectId, sceneId)}/${variantId}/preview`;
}

export async function getKeyframeApproval(projectId: string, sceneId: string, signal?: AbortSignal): Promise<KeyframeApprovalStatusResponse> {
  return readJson<KeyframeApprovalStatusResponse>(await fetch(`${sceneUrl(projectId, sceneId)}/approval`, { headers: { Accept: "application/json" }, signal }), "Keyframe approval");
}

export async function approveKeyframes(projectId: string, sceneId: string): Promise<KeyframeApprovalStatusResponse> {
  return readJson<KeyframeApprovalStatusResponse>(await fetch(`${sceneUrl(projectId, sceneId)}/approval`, {
    method: "POST",
    headers: { Accept: "application/json" },
  }), "Approve keyframes");
}

export async function revokeKeyframeApproval(projectId: string, sceneId: string): Promise<void> {
  const response = await fetch(`${sceneUrl(projectId, sceneId)}/approval`, { method: "DELETE" });
  if (response.status === 404) return;
  if (!response.ok) await readJson<unknown>(response, "Revoke keyframe approval");
}
