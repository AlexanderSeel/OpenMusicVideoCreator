export type TimelineTransitionKind = "Cut" | "Fade" | "Crossfade";
export type TimelineEffectKind = "FadeToBlack" | "Vignette" | "Grayscale";

export interface TimelineClipTransform {
  scale: number;
  positionX: number;
  positionY: number;
  cropLeft: number;
  cropTop: number;
  cropRight: number;
  cropBottom: number;
  opacity: number;
}

export interface TimelineColorAdjustment {
  brightness: number;
  contrast: number;
  saturation: number;
}

export interface TimelineClip {
  id: string;
  sceneId: string;
  sequence: number;
  clipVariantId: string;
  mediaAssetId: string;
  timelineStartSeconds: number;
  timelineDurationSeconds: number;
  sourceInSeconds: number;
  sourceDurationSeconds: number;
  playbackRate: number;
  freezeExtensionSeconds: number;
  transitionIn: TimelineTransitionKind;
  transitionDurationSeconds: number;
  transform: TimelineClipTransform;
  color: TimelineColorAdjustment;
}

export interface TimelineOverlay {
  id: string;
  mediaAssetId: string;
  startSeconds: number;
  endSeconds: number;
  positionX: number;
  positionY: number;
  scale: number;
  opacity: number;
}

export interface TimelineEffect {
  id: string;
  kind: TimelineEffectKind;
  startSeconds: number;
  endSeconds: number;
  strength: number;
}

export interface ProjectTimelineVersion {
  id: string;
  projectId: string;
  storyboardVersionId: string;
  songMediaAssetId: string;
  version: number;
  parentVersionId?: string | null;
  musicTrackLocked: boolean;
  clips: TimelineClip[];
  overlays: TimelineOverlay[];
  effects: TimelineEffect[];
  createdUtc: string;
}

export interface TimelineClipEdit {
  sourceInSeconds: number;
  sourceDurationSeconds: number;
  playbackRate: number;
  freezeExtensionSeconds: number;
  transitionIn: TimelineTransitionKind;
  transitionDurationSeconds: number;
  transform: TimelineClipTransform;
  color: TimelineColorAdjustment;
}

export interface TimelineOverlayEdit {
  id?: string | null;
  mediaAssetId: string;
  startSeconds: number;
  endSeconds: number;
  positionX: number;
  positionY: number;
  scale: number;
  opacity: number;
}

export interface TimelineEffectEdit {
  id?: string | null;
  kind: TimelineEffectKind;
  startSeconds: number;
  endSeconds: number;
  strength: number;
}

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";
const base = (projectId: string) => `${apiBaseUrl}/api/projects/${projectId}/timeline`;

async function readJson<T>(response: Response, context: string): Promise<T> {
  if (!response.ok) {
    let detail = "";
    try {
      const body = (await response.json()) as { error?: string; detail?: string; title?: string };
      detail = body.error ?? body.detail ?? body.title ?? "";
    } catch {
      // HTTP status remains the fallback.
    }
    throw new Error(`${context} failed with HTTP ${response.status}${detail ? `: ${detail}` : ""}`);
  }
  return (await response.json()) as T;
}

export async function initializeTimeline(projectId: string, signal?: AbortSignal): Promise<ProjectTimelineVersion> {
  return readJson<ProjectTimelineVersion>(await fetch(`${base(projectId)}/initialize`, {
    method: "POST",
    headers: { Accept: "application/json" },
    signal,
  }), "Initialize timeline");
}

export async function listTimelineVersions(projectId: string, signal?: AbortSignal): Promise<ProjectTimelineVersion[]> {
  return readJson<ProjectTimelineVersion[]>(await fetch(`${base(projectId)}/versions`, {
    headers: { Accept: "application/json" },
    signal,
  }), "Timeline versions");
}

export async function resetTimeline(projectId: string): Promise<ProjectTimelineVersion> {
  return readJson<ProjectTimelineVersion>(await fetch(`${base(projectId)}/reset`, {
    method: "POST",
    headers: { Accept: "application/json" },
  }), "Reset timeline");
}

export async function updateTimelineClip(projectId: string, clipId: string, edit: TimelineClipEdit): Promise<ProjectTimelineVersion> {
  return readJson<ProjectTimelineVersion>(await fetch(`${base(projectId)}/clips/${clipId}`, {
    method: "PUT",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify(edit),
  }), "Update timeline clip");
}

export async function reorderTimelineClips(projectId: string, clipIds: string[]): Promise<ProjectTimelineVersion> {
  return readJson<ProjectTimelineVersion>(await fetch(`${base(projectId)}/clips/reorder`, {
    method: "POST",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify({ clipIds }),
  }), "Reorder timeline clips");
}

export async function replaceTimelineClip(projectId: string, clipId: string, clipVariantId: string): Promise<ProjectTimelineVersion> {
  return readJson<ProjectTimelineVersion>(await fetch(`${base(projectId)}/clips/${clipId}/replace`, {
    method: "POST",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify({ clipVariantId }),
  }), "Replace timeline clip");
}

export async function splitTimelineClip(projectId: string, clipId: string, splitAtSeconds: number): Promise<ProjectTimelineVersion> {
  return readJson<ProjectTimelineVersion>(await fetch(`${base(projectId)}/clips/${clipId}/split`, {
    method: "POST",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify({ splitAtSeconds }),
  }), "Split timeline clip");
}

export async function upsertTimelineOverlay(projectId: string, edit: TimelineOverlayEdit): Promise<ProjectTimelineVersion> {
  return readJson<ProjectTimelineVersion>(await fetch(`${base(projectId)}/overlays`, {
    method: "PUT",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify(edit),
  }), "Save timeline overlay");
}

export async function deleteTimelineOverlay(projectId: string, overlayId: string): Promise<ProjectTimelineVersion> {
  return readJson<ProjectTimelineVersion>(await fetch(`${base(projectId)}/overlays/${overlayId}`, {
    method: "DELETE",
    headers: { Accept: "application/json" },
  }), "Delete timeline overlay");
}

export async function upsertTimelineEffect(projectId: string, edit: TimelineEffectEdit): Promise<ProjectTimelineVersion> {
  return readJson<ProjectTimelineVersion>(await fetch(`${base(projectId)}/effects`, {
    method: "PUT",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify(edit),
  }), "Save timeline effect");
}

export async function deleteTimelineEffect(projectId: string, effectId: string): Promise<ProjectTimelineVersion> {
  return readJson<ProjectTimelineVersion>(await fetch(`${base(projectId)}/effects/${effectId}`, {
    method: "DELETE",
    headers: { Accept: "application/json" },
  }), "Delete timeline effect");
}

export async function restoreTimelineVersion(projectId: string, versionId: string): Promise<ProjectTimelineVersion> {
  return readJson<ProjectTimelineVersion>(await fetch(`${base(projectId)}/restore/${versionId}`, {
    method: "POST",
    headers: { Accept: "application/json" },
  }), "Restore timeline version");
}
