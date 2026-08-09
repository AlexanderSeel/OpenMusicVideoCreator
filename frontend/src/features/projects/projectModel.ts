import type { ProjectResponse, ProjectUpsertRequest } from "@/src/api/client";

export type EditorState = ProjectUpsertRequest & { id?: string };

export function createEmptyProject(): EditorState {
  return {
    title: "",
    artist: "",
    lyrics: "",
    storyline: "",
    meaning: "",
    visualDirection: "",
    mood: "",
    genre: "",
    aspectRatio: "Landscape16x9",
    resolutionWidth: 1920,
    resolutionHeight: 1080,
    targetPlatforms: ["YouTube"],
    preset: "Balanced",
    estimatedBudget: null,
    maximumBudget: null,
    references: [],
  };
}

export function projectToEditor(project: ProjectResponse): EditorState {
  return {
    ...project,
    references: project.references,
  };
}

export function editorToRequest(editor: EditorState): ProjectUpsertRequest {
  const request: EditorState = { ...editor };
  delete request.id;
  return request;
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024 * 1024) {
    return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  }

  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
