"use client";

import { FormEvent, useEffect, useMemo, useState } from "react";
import {
  createProject,
  deleteProject,
  getProjectSong,
  listProjects,
  updateProject,
  uploadProjectSong,
  type ProjectResponse,
  type ProjectSongResponse,
  type ProjectUpsertRequest,
} from "@/src/api/client";

type EditorState = ProjectUpsertRequest & { id?: string };
type LoadState = "loading" | "ready" | "error";

const emptyProject = (): EditorState => ({
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
});

function toEditor(project: ProjectResponse): EditorState {
  return {
    ...project,
    references: project.references as ProjectUpsertRequest["references"],
  };
}

function formatBytes(bytes: number): string {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function ProjectStudio() {
  const [projects, setProjects] = useState<ProjectResponse[]>([]);
  const [editor, setEditor] = useState<EditorState>(emptyProject);
  const [selectedSong, setSelectedSong] = useState<File | null>(null);
  const [song, setSong] = useState<ProjectSongResponse | null>(null);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [message, setMessage] = useState<string>("");
  const [saving, setSaving] = useState(false);
  const [online, setOnline] = useState(true);

  const selectedProject = useMemo(
    () => projects.find((project) => project.id === editor.id),
    [projects, editor.id],
  );

  async function refreshProjects(signal?: AbortSignal) {
    setLoadState("loading");
    try {
      const result = await listProjects(signal);
      setProjects(result);
      setLoadState("ready");
      if (!editor.id && result.length > 0) {
        await openProject(result[0], signal);
      }
    } catch (error) {
      if (signal?.aborted) return;
      setLoadState("error");
      setMessage(error instanceof Error ? error.message : "Could not load projects.");
    }
  }

  async function openProject(project: ProjectResponse, signal?: AbortSignal) {
    setEditor(toEditor(project));
    setSelectedSong(null);
    setMessage("");
    try {
      setSong(await getProjectSong(project.id, signal));
    } catch (error) {
      if (!signal?.aborted) setMessage(error instanceof Error ? error.message : "Could not load song metadata.");
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
    // Initial project bootstrap only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function setField<K extends keyof EditorState>(key: K, value: EditorState[K]) {
    setEditor((current) => ({ ...current, [key]: value }));
  }

  function beginNewProject() {
    setEditor(emptyProject());
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
      const { id, ...request } = editor;
      let saved = id ? await updateProject(id, request) : await createProject(request);
      if (selectedSong) {
        setSong(await uploadProjectSong(saved.id, selectedSong));
        saved = { ...saved, references: saved.references };
      }
      const nextProjects = await listProjects();
      setProjects(nextProjects);
      const refreshed = nextProjects.find((project) => project.id === saved.id) ?? saved;
      setEditor(toEditor(refreshed));
      setSelectedSong(null);
      setMessage(id ? "Project saved." : "Project created.");
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
      else setEditor(emptyProject());
      setMessage("Project deleted.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not delete project.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <main className="studio-shell">
      <aside className="studio-sidebar" aria-label="Projects">
        <div className="brand-block">
          <span className="brand-mark">OM</span>
          <div>
            <strong>OpenMusicVideoCreator</strong>
            <span>AI music video studio</span>
          </div>
        </div>

        <button className="button button-primary button-full" type="button" onClick={beginNewProject}>+ New project</button>

        <div className="sidebar-heading">
          <span>Projects</span>
          <span className="count-pill">{projects.length}</span>
        </div>

        {loadState === "loading" ? <p className="muted" aria-live="polite">Loading projects…</p> : null}
        {loadState === "error" ? (
          <div className="inline-state" role="alert">
            <p>{message}</p>
            <button className="button button-ghost" type="button" onClick={() => void refreshProjects()}>Retry</button>
          </div>
        ) : null}
        {loadState === "ready" && projects.length === 0 ? <p className="muted">No projects yet. Create the first video.</p> : null}

        <nav className="project-list" aria-label="Saved projects">
          {projects.map((project) => (
            <button
              key={project.id}
              className={`project-item ${editor.id === project.id ? "is-active" : ""}`}
              type="button"
              onClick={() => void openProject(project)}
              aria-current={editor.id === project.id ? "page" : undefined}
            >
              <strong>{project.title}</strong>
              <span>{project.artist || "Unknown artist"}</span>
              <small>{project.preset.replace("BestQuality", "Best Quality")}</small>
            </button>
          ))}
        </nav>
      </aside>

      <section className="studio-workspace">
        <header className="workspace-header">
          <div>
            <p className="eyebrow">Project workspace</p>
            <h1>{editor.id ? editor.title || "Untitled project" : "Create a music video"}</h1>
          </div>
          <div className={`network-status ${online ? "is-online" : "is-offline"}`} aria-live="polite">
            <span aria-hidden="true" />{online ? "Connected" : "Offline"}
          </div>
        </header>

        <div className="mode-tabs" role="tablist" aria-label="Editor mode">
          <button role="tab" aria-selected="true" className="mode-tab is-active" type="button">Simple</button>
          <button role="tab" aria-selected="false" className="mode-tab" type="button" disabled>Advanced <span>later</span></button>
          <button role="tab" aria-selected="false" className="mode-tab" type="button" disabled>Expert / Custom <span>later</span></button>
        </div>

        <form className="editor-form" onSubmit={save}>
          {!online ? <div className="status-banner warning" role="status">You are offline. Existing data stays visible; saving resumes when the API is reachable.</div> : null}
          {message ? <div className="status-banner" role="status">{message}</div> : null}

          <section className="form-section" aria-labelledby="identity-heading">
            <div className="section-heading"><div><span>01</span><h2 id="identity-heading">Song & identity</h2></div><p>Start with the music and the words the video must serve.</p></div>
            <div className="field-grid two-columns">
              <label className="field"><span>Project title *</span><input required value={editor.title} onChange={(event) => setField("title", event.target.value)} placeholder="In the Next Life" /></label>
              <label className="field"><span>Artist</span><input value={editor.artist} onChange={(event) => setField("artist", event.target.value)} placeholder="Artist name" /></label>
            </div>
            <label className="upload-card">
              <input type="file" accept="audio/*,.mp3,.wav,.m4a,.aac,.flac,.ogg,.opus,.webm" onChange={(event) => setSelectedSong(event.target.files?.[0] ?? null)} />
              <div><strong>{selectedSong ? selectedSong.name : song ? "Song attached" : "Choose song"}</strong><span>{selectedSong ? formatBytes(selectedSong.size) : song ? `${song.mimeType} • ${formatBytes(song.fileSize)}` : "MP3, WAV, M4A, AAC, FLAC, OGG, OPUS or WebM"}</span></div>
              <span className="upload-action">Browse</span>
            </label>
            <label className="field"><span>Lyrics</span><textarea rows={8} value={editor.lyrics} onChange={(event) => setField("lyrics", event.target.value)} placeholder="Paste the authoritative lyrics here…" /></label>
          </section>

          <section className="form-section" aria-labelledby="story-heading">
            <div className="section-heading"><div><span>02</span><h2 id="story-heading">Meaning & visual direction</h2></div><p>Tell the Director what the song means before describing shots.</p></div>
            <div className="field-grid two-columns">
              <label className="field"><span>Storyline</span><textarea rows={5} value={editor.storyline} onChange={(event) => setField("storyline", event.target.value)} placeholder="What happens across the video?" /></label>
              <label className="field"><span>Meaning</span><textarea rows={5} value={editor.meaning} onChange={(event) => setField("meaning", event.target.value)} placeholder="What should the viewer feel or understand?" /></label>
            </div>
            <label className="field"><span>Visual direction</span><textarea rows={4} value={editor.visualDirection} onChange={(event) => setField("visualDirection", event.target.value)} placeholder="Mystic, intimate, cinematic, restrained camera…" /></label>
            <div className="field-grid two-columns">
              <label className="field"><span>Mood</span><input value={editor.mood} onChange={(event) => setField("mood", event.target.value)} placeholder="Hopeful, melancholic, surreal" /></label>
              <label className="field"><span>Genre</span><input value={editor.genre} onChange={(event) => setField("genre", event.target.value)} placeholder="D&B, rap, trance" /></label>
            </div>
          </section>

          <section className="form-section" aria-labelledby="references-heading">
            <div className="section-heading"><div><span>03</span><h2 id="references-heading">Visual references</h2></div><p>Reusable libraries arrive in Block 7; Simple Mode already reserves their place.</p></div>
            <div className="reference-grid">
              {[
                ["Character", "Keep faces, outfits and identity consistent"],
                ["Style", "Define visual language, lighting and camera feel"],
                ["Location", "Reuse environments and continuity constraints"],
              ].map(([title, description]) => <button key={title} className="reference-card" type="button" disabled><span className="reference-icon">+</span><strong>{title}</strong><small>{description}</small><em>Library coming in Block 7</em></button>)}
            </div>
          </section>

          <section className="form-section" aria-labelledby="output-heading">
            <div className="section-heading"><div><span>04</span><h2 id="output-heading">Output & generation strategy</h2></div><p>Choose intent-level settings; provider details stay hidden in Simple Mode.</p></div>
            <div className="field-grid three-columns">
              <label className="field"><span>Aspect ratio</span><select value={editor.aspectRatio} onChange={(event) => setField("aspectRatio", event.target.value as EditorState["aspectRatio"])}><option value="Landscape16x9">16:9 Landscape</option><option value="Portrait9x16">9:16 Portrait</option><option value="Square1x1">1:1 Square</option></select></label>
              <label className="field"><span>Preset</span><select value={editor.preset} onChange={(event) => setField("preset", event.target.value as EditorState["preset"])}><option value="Fast">Fast</option><option value="Balanced">Balanced</option><option value="BestQuality">Best Quality</option><option value="Cheapest">Cheapest</option><option value="Custom">Custom</option></select></label>
              <label className="field"><span>Target platform</span><select value={editor.targetPlatforms?.[0] ?? "YouTube"} onChange={(event) => setField("targetPlatforms", [event.target.value])}><option>YouTube</option><option>TikTok</option><option>Instagram</option><option>Vimeo</option><option>Local file</option></select></label>
            </div>
            <div className="field-grid two-columns">
              <label className="field"><span>Estimated budget</span><div className="money-input"><span>€</span><input type="number" min="0" step="0.01" value={editor.estimatedBudget ?? ""} onChange={(event) => setField("estimatedBudget", event.target.value === "" ? null : Number(event.target.value))} /></div></label>
              <label className="field"><span>Maximum budget</span><div className="money-input"><span>€</span><input type="number" min="0" step="0.01" value={editor.maximumBudget ?? ""} onChange={(event) => setField("maximumBudget", event.target.value === "" ? null : Number(event.target.value))} /></div></label>
            </div>
          </section>

          <footer className="editor-actions">
            <div><strong>{selectedProject ? "Editing saved project" : "New project"}</strong><span>{editor.id ? "Changes persist in DuckDB when saved." : "Create the project before analysis begins."}</span></div>
            <div className="action-buttons">
              {editor.id ? <button className="button button-danger" type="button" disabled={saving} onClick={() => void removeCurrentProject()}>Delete</button> : null}
              <button className="button button-primary" type="submit" disabled={saving || !online}>{saving ? "Saving…" : editor.id ? "Save project" : "Create project"}</button>
            </div>
          </footer>
        </form>
      </section>
    </main>
  );
}
