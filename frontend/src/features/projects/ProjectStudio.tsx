"use client";

import { type FormEvent, useEffect, useState } from "react";
import {
  createProject,
  deleteProject,
  getProjectSong,
  listProjects,
  listVisualLibrary,
  updateProject,
  uploadProjectSong,
  type ProjectResponse,
  type ProjectSongResponse,
  type VisualLibraryResponse,
} from "@/src/api/client";
import { SongAnalysisPanel } from "@/src/features/analysis/SongAnalysisPanel";
import { GenerationQueuePanel } from "@/src/features/generation/GenerationQueuePanel";
import { KeyframeWorkspace, type StudioMode } from "@/src/features/generation/KeyframeWorkspace";
import { VideoGenerationWorkspace } from "@/src/features/generation/VideoGenerationWorkspace";
import { VisualLibraryPanel } from "@/src/features/library/VisualLibraryPanel";
import { DirectorStoryboardPanel } from "@/src/features/planning/DirectorStoryboardPanel";
import { ProjectRenderWorkspace } from "@/src/features/rendering/ProjectRenderWorkspace";
import { AdvancedTimelineAnalysisPanel } from "@/src/features/timeline/AdvancedTimelineAnalysisPanel";
import { AdvancedTimelineEditor } from "@/src/features/timeline/AdvancedTimelineEditor";
import { ProjectForm } from "./ProjectForm";
import { ProjectSidebar } from "./ProjectSidebar";
import { createEmptyProject, editorToRequest, projectToEditor, type EditorState } from "./projectModel";

type LoadState = "loading" | "ready" | "error";

export function ProjectStudio() {
  const [projects, setProjects] = useState<ProjectResponse[]>([]);
  const [visualLibrary, setVisualLibrary] = useState<VisualLibraryResponse[]>([]);
  const [editor, setEditor] = useState<EditorState>(createEmptyProject);
  const [selectedSong, setSelectedSong] = useState<File | null>(null);
  const [song, setSong] = useState<ProjectSongResponse | null>(null);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [loadError, setLoadError] = useState("");
  const [message, setMessage] = useState("");
  const [saving, setSaving] = useState(false);
  const [online, setOnline] = useState(true);
  const [mode, setMode] = useState<StudioMode>("Simple");

  async function openProject(project: ProjectResponse, signal?: AbortSignal) {
    setEditor(projectToEditor(project));
    setSelectedSong(null);
    setMessage("");
    try {
      setSong(await getProjectSong(project.id, signal));
    } catch (error) {
      if (!signal?.aborted) setMessage(error instanceof Error ? error.message : "Could not load song metadata.");
    }
  }

  async function refreshProjects(signal?: AbortSignal) {
    setLoadState("loading");
    setLoadError("");
    try {
      const [result, library] = await Promise.all([listProjects(signal), listVisualLibrary(undefined, signal)]);
      setProjects(result);
      setVisualLibrary(library);
      setLoadState("ready");
      if (!editor.id && result.length > 0) await openProject(result[0], signal);
    } catch (error) {
      if (signal?.aborted) return;
      setLoadState("error");
      setLoadError(error instanceof Error ? error.message : "Could not load projects.");
    }
  }

  useEffect(() => {
    const controller = new AbortController();
    void refreshProjects(controller.signal);
    const updateNetworkState = () => setOnline(navigator.onLine);
    updateNetworkState();
    window.addEventListener("online", updateNetworkState);
    window.addEventListener("offline", updateNetworkState);
    return () => {
      controller.abort();
      window.removeEventListener("online", updateNetworkState);
      window.removeEventListener("offline", updateNetworkState);
    };
    // Project bootstrap intentionally runs once for the mounted studio.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function setField<K extends keyof EditorState>(key: K, value: EditorState[K]) {
    setEditor((current) => ({ ...current, [key]: value }));
  }

  function beginNewProject() {
    setEditor(createEmptyProject());
    setSong(null);
    setSelectedSong(null);
    setMessage("");
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!online || saving) return;
    setSaving(true);
    setMessage("");
    try {
      const projectId = editor.id;
      const request = editorToRequest(editor);
      const saved = projectId ? await updateProject(projectId, request) : await createProject(request);
      if (selectedSong) setSong(await uploadProjectSong(saved.id, selectedSong));
      const nextProjects = await listProjects();
      setProjects(nextProjects);
      const refreshed = nextProjects.find((project) => project.id === saved.id) ?? saved;
      setEditor(projectToEditor(refreshed));
      setSelectedSong(null);
      setMessage(projectId ? "Project saved." : "Project created.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not save project.");
    } finally {
      setSaving(false);
    }
  }

  async function removeCurrentProject() {
    if (!editor.id || !window.confirm(`Delete “${editor.title}”? Generated media is not silently removed.`)) return;
    setSaving(true);
    try {
      await deleteProject(editor.id);
      const nextProjects = await listProjects();
      setProjects(nextProjects);
      setSong(null);
      setSelectedSong(null);
      if (nextProjects.length > 0) await openProject(nextProjects[0]);
      else setEditor(createEmptyProject());
      setMessage("Project deleted.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not delete project.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <main className="studio-shell">
      <ProjectSidebar projects={projects} selectedProjectId={editor.id} loading={loadState === "loading"} error={loadState === "error" ? loadError : undefined} onCreateNew={beginNewProject} onSelect={(project) => void openProject(project)} onRetry={() => void refreshProjects()} />

      <section className="studio-workspace">
        <header className="workspace-header">
          <div><p className="eyebrow">Project workspace</p><h1>{editor.id ? editor.title || "Untitled project" : "Create a music video"}</h1></div>
          <div className={`network-status ${online ? "is-online" : "is-offline"}`} aria-live="polite"><span aria-hidden="true" />{online ? "Connected" : "Offline"}</div>
        </header>

        <div className="mode-tabs" role="tablist" aria-label="Editor mode">
          {(["Simple", "Advanced", "Custom"] as StudioMode[]).map((candidate) => (
            <button key={candidate} role="tab" aria-selected={mode === candidate} className={`mode-tab ${mode === candidate ? "is-active" : ""}`} type="button" onClick={() => setMode(candidate)}>{candidate === "Custom" ? "Expert / Custom" : candidate}{candidate !== "Simple" ? <span>generation + timeline</span> : null}</button>
          ))}
        </div>

        <ProjectForm editor={editor} selectedSong={selectedSong} song={song} visualLibrary={visualLibrary} online={online} saving={saving} message={message} onFieldChange={setField} onSongSelected={setSelectedSong} onSubmit={save} onDelete={() => void removeCurrentProject()} />
        <SongAnalysisPanel projectId={editor.id} songAttached={song !== null} lyrics={editor.lyrics} />
        <VisualLibraryPanel onChanged={setVisualLibrary} />
        <DirectorStoryboardPanel projectId={editor.id} visualLibrary={visualLibrary} />
        <KeyframeWorkspace projectId={editor.id} mode={mode} />
        <VideoGenerationWorkspace projectId={editor.id} mode={mode} />
        <GenerationQueuePanel projectId={editor.id} mode={mode} />
        {mode !== "Simple" ? <><AdvancedTimelineAnalysisPanel projectId={editor.id} /><AdvancedTimelineEditor projectId={editor.id} /></> : null}
        <ProjectRenderWorkspace projectId={editor.id} />
      </section>
    </main>
  );
}
