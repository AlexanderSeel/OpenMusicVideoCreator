"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  listProjectRenders,
  projectRenderOutputUrl,
  queueProjectRender,
  type ProjectRenderKind,
  type ProjectRenderRecord,
} from "@/src/api/renders";

interface ProjectRenderWorkspaceProps {
  projectId?: string;
}

const activeStates = new Set(["Planned", "Queued", "Rendering"]);

export function ProjectRenderWorkspace({ projectId }: ProjectRenderWorkspaceProps) {
  const [renders, setRenders] = useState<ProjectRenderRecord[]>([]);
  const [busyKind, setBusyKind] = useState<ProjectRenderKind | null>(null);
  const [message, setMessage] = useState("");

  const refresh = useCallback(async (signal?: AbortSignal) => {
    if (!projectId) {
      setRenders([]);
      return;
    }
    try {
      setRenders(await listProjectRenders(projectId, signal));
    } catch (error) {
      if (!signal?.aborted) setMessage(error instanceof Error ? error.message : "Could not load render history.");
    }
  }, [projectId]);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);
    return () => controller.abort();
  }, [refresh]);

  const hasActive = useMemo(() => renders.some((render) => activeStates.has(render.state)), [renders]);
  useEffect(() => {
    if (!projectId || !hasActive) return;
    const timer = window.setInterval(() => void refresh(), 1500);
    return () => window.clearInterval(timer);
  }, [hasActive, projectId, refresh]);

  async function queue(kind: ProjectRenderKind) {
    if (!projectId || busyKind) return;
    setBusyKind(kind);
    setMessage("");
    try {
      const render = await queueProjectRender(projectId, kind);
      setRenders((current) => [render, ...current.filter((item) => item.id !== render.id)]);
      setMessage(`${kind} render v${render.version} queued from the current selected clip timeline.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : `Could not queue ${kind.toLowerCase()} render.`);
    } finally {
      setBusyKind(null);
    }
  }

  if (!projectId) {
    return (
      <section className="render-workspace render-empty" aria-labelledby="render-heading">
        <RenderHeading />
        <p>Save a project before creating preview or final renders.</p>
      </section>
    );
  }

  return (
    <section className="render-workspace" aria-labelledby="render-heading">
      <RenderHeading />
      {message ? <div className="status-banner" role="status">{message}</div> : null}

      <div className="render-actions">
        <button className="button" type="button" disabled={busyKind !== null} onClick={() => void queue("Preview")}>{busyKind === "Preview" ? "Queuing…" : "Render preview"}</button>
        <button className="button button-primary" type="button" disabled={busyKind !== null} onClick={() => void queue("Final")}>{busyKind === "Final" ? "Queuing…" : "Render final MP4"}</button>
        <p>Both outputs use the same selected scene timeline and original uploaded Song. Preview uses a smaller/faster encoding profile.</p>
      </div>

      <div className="render-history" aria-label="Render history">
        {renders.length === 0 ? <p className="render-empty-state">No renders yet. Every render is versioned and non-destructive.</p> : renders.map((render) => (
          <article className="render-card" key={render.id}>
            <div className="render-card-heading">
              <div><strong>{render.manifest.kind} · v{render.version}</strong><span>{render.manifest.width}×{render.manifest.height} · {formatDuration(render.manifest.durationSeconds)} · {render.manifest.clips.length} scenes</span></div>
              <span className={`render-state state-${render.state.toLowerCase()}`}>{render.state}</span>
            </div>
            <dl className="render-provenance">
              <div><dt>Timeline</dt><dd title={render.manifest.timelineSha256}>{render.manifest.timelineSha256.slice(0, 12)}…</dd></div>
              <div><dt>Storyboard</dt><dd title={render.manifest.storyboardVersionId}>{render.manifest.storyboardVersionId.slice(0, 8)}…</dd></div>
              <div><dt>Original song</dt><dd title={render.manifest.songMediaAssetId}>{render.manifest.songMediaAssetId.slice(0, 8)}…</dd></div>
            </dl>
            {render.errorMessage ? <p className="render-error">{render.errorMessage}</p> : null}
            {render.state === "Completed" ? <a className="button" href={projectRenderOutputUrl(projectId, render.id)}>Download {render.manifest.kind === "Preview" ? "preview" : "final MP4"}</a> : null}
            {render.commandLog ? <details><summary>Deterministic FFmpeg command</summary><pre>{render.commandLog}</pre></details> : null}
          </article>
        ))}
      </div>
    </section>
  );
}

function RenderHeading() {
  return <div className="structure-heading"><div><p className="eyebrow">Assembly & export</p><h2 id="render-heading">Project render</h2><span>Selected scene clips → deterministic timeline → original Song audio → MP4.</span></div></div>;
}

function formatDuration(seconds: number): string {
  const whole = Math.max(0, Math.round(seconds));
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, "0")}`;
}
