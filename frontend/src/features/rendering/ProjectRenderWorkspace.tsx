"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  cancelProjectRender,
  listProjectRenders,
  projectRenderOutputUrl,
  queueProjectRender,
  retryProjectRender,
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
  const [busyRenderId, setBusyRenderId] = useState<string | null>(null);
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
    if (!projectId || busyKind || busyRenderId) return;
    setBusyKind(kind);
    setMessage("");
    try {
      const render = await queueProjectRender(projectId, kind);
      mergeRender(render);
      setMessage(`${kind} render v${render.version} queued from the current selected clip timeline.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : `Could not queue ${kind.toLowerCase()} render.`);
    } finally {
      setBusyKind(null);
    }
  }

  async function cancel(render: ProjectRenderRecord) {
    if (!projectId || busyRenderId) return;
    setBusyRenderId(render.id);
    setMessage("");
    try {
      const updated = await cancelProjectRender(projectId, render.id);
      mergeRender(updated);
      setMessage(`${render.manifest.kind} render v${render.version} cancelled.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not cancel render.");
    } finally {
      setBusyRenderId(null);
    }
  }

  async function retry(render: ProjectRenderRecord) {
    if (!projectId || busyRenderId) return;
    setBusyRenderId(render.id);
    setMessage("");
    try {
      const updated = await retryProjectRender(projectId, render.id);
      mergeRender(updated);
      setMessage(`${render.manifest.kind} render v${render.version} re-queued without changing its timeline manifest.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not retry render.");
    } finally {
      setBusyRenderId(null);
    }
  }

  function mergeRender(render: ProjectRenderRecord) {
    setRenders((current) => [render, ...current.filter((item) => item.id !== render.id)]
      .sort((left, right) => right.version - left.version));
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
        <button className="button" type="button" disabled={busyKind !== null || busyRenderId !== null} onClick={() => void queue("Preview")}>{busyKind === "Preview" ? "Queuing…" : "Render preview"}</button>
        <button className="button button-primary" type="button" disabled={busyKind !== null || busyRenderId !== null} onClick={() => void queue("Final")}>{busyKind === "Final" ? "Queuing…" : "Render final MP4"}</button>
        <p>Both outputs use the same selected scene/Advanced timeline decisions and original uploaded Song. Preview uses a smaller/faster encoding profile.</p>
      </div>

      <div className="render-history" aria-label="Render history">
        {renders.length === 0 ? <p className="render-empty-state">No renders yet. Every render is versioned and non-destructive.</p> : renders.map((render) => {
          const attempts = render.attempts ?? [];
          const busy = busyRenderId === render.id;
          const overlays = render.manifest.overlays ?? [];
          const effects = render.manifest.effects ?? [];
          return (
            <article className="render-card" key={render.id}>
              <div className="render-card-heading">
                <div><strong>{render.manifest.kind} · v{render.version}</strong><span>{render.manifest.width}×{render.manifest.height} · {formatDuration(render.manifest.durationSeconds)} · {render.manifest.clips.length} scenes · {overlays.length} overlays · {effects.length} effects</span></div>
                <span className={`render-state state-${render.state.toLowerCase()}`}>{render.state}</span>
              </div>
              <dl className="render-provenance">
                <div><dt>Decision hash</dt><dd title={render.manifest.timelineSha256}>{render.manifest.timelineSha256.slice(0, 12)}…</dd></div>
                <div><dt>Advanced timeline</dt><dd title={render.manifest.timelineVersionId ?? "Storyboard baseline"}>{render.manifest.timelineVersionId ? `${render.manifest.timelineVersionId.slice(0, 8)}…` : "Storyboard baseline"}</dd></div>
                <div><dt>Storyboard</dt><dd title={render.manifest.storyboardVersionId}>{render.manifest.storyboardVersionId.slice(0, 8)}…</dd></div>
                <div><dt>Original song</dt><dd title={render.manifest.songMediaAssetId}>{render.manifest.songMediaAssetId.slice(0, 8)}…</dd></div>
                <div><dt>Attempts</dt><dd>{attempts.length}</dd></div>
              </dl>
              {render.errorMessage ? <p className="render-error">{render.errorMessage}</p> : null}
              <div className="render-card-actions">
                {render.state === "Completed" ? <a className="button" href={projectRenderOutputUrl(projectId, render.id)}>Download {render.manifest.kind === "Preview" ? "preview" : "final MP4"}</a> : null}
                {activeStates.has(render.state) ? <button className="button" type="button" disabled={busy} onClick={() => void cancel(render)}>{busy ? "Cancelling…" : "Cancel render"}</button> : null}
                {render.state === "Failed" || render.state === "Cancelled" ? <button className="button" type="button" disabled={busy} onClick={() => void retry(render)}>{busy ? "Re-queuing…" : "Retry same render"}</button> : null}
              </div>
              {attempts.length > 0 ? (
                <details>
                  <summary>Render attempts ({attempts.length})</summary>
                  <ol className="render-attempts">
                    {attempts.map((attempt) => (
                      <li key={attempt.attemptNumber}>
                        <strong>Attempt {attempt.attemptNumber} · {attempt.state}</strong>
                        <span>{formatTimestamp(attempt.startedUtc)}{attempt.completedUtc ? ` → ${formatTimestamp(attempt.completedUtc)}` : " · running"}</span>
                        {attempt.errorMessage ? <p className="render-error">{attempt.errorMessage}</p> : null}
                        {attempt.commandLog ? <pre>{attempt.commandLog}</pre> : null}
                      </li>
                    ))}
                  </ol>
                </details>
              ) : null}
              {render.commandLog ? <details><summary>Deterministic FFmpeg command</summary><pre>{render.commandLog}</pre></details> : null}
            </article>
          );
        })}
      </div>
    </section>
  );
}

function RenderHeading() {
  return <div className="structure-heading"><div><p className="eyebrow">Assembly & export</p><h2 id="render-heading">Project render</h2><span>Versioned scene/timeline decisions → original Song audio → deterministic MP4.</span></div></div>;
}

function formatDuration(seconds: number): string {
  const whole = Math.max(0, Math.round(seconds));
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, "0")}`;
}

function formatTimestamp(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
