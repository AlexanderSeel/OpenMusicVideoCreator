"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  cancelJob,
  cancelProjectJobs,
  jobEventsUrl,
  listJobs,
  pauseJob,
  pauseProjectJobs,
  restartJob,
  resumeJob,
  resumeProjectJobs,
  retryJob,
  type JobResponse,
  type JobState,
} from "@/src/api/jobs";
import type { StudioMode } from "./KeyframeWorkspace";

interface GenerationQueuePanelProps {
  projectId?: string;
  mode: StudioMode;
}

const terminal = new Set<JobState>(["Completed", "Rejected", "FailedPermanent", "Cancelled"]);
const retryable = new Set<JobState>(["FailedRetryable", "RetryScheduled", "WaitingForQuota", "WaitingForProvider"]);

export function GenerationQueuePanel({ projectId, mode }: GenerationQueuePanelProps) {
  const [jobs, setJobs] = useState<JobResponse[]>([]);
  const [showAll, setShowAll] = useState(false);
  const [connected, setConnected] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [, setClock] = useState(0);
  const advanced = mode !== "Simple";

  const reload = useCallback(async (signal?: AbortSignal) => {
    setJobs(await listJobs(signal));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    reload(controller.signal).catch((error: unknown) => {
      if (!controller.signal.aborted) setMessage(error instanceof Error ? error.message : "Could not load generation queue.");
    });
    return () => controller.abort();
  }, [reload]);

  useEffect(() => {
    const source = new EventSource(jobEventsUrl());
    source.addEventListener("ready", () => {
      setConnected(true);
      void reload().catch(() => undefined);
    });
    source.addEventListener("job", (event) => {
      try {
        const changed = JSON.parse((event as MessageEvent<string>).data) as JobResponse;
        setJobs((current) => current.some((job) => job.id === changed.id)
          ? current.map((job) => job.id === changed.id ? changed : job)
          : [...current, changed]);
      } catch {
        // A malformed notification is ignored; the next ready/reload restores persisted truth.
      }
    });
    source.onerror = () => setConnected(false);
    return () => source.close();
  }, [reload]);

  useEffect(() => {
    const timer = window.setInterval(() => setClock((value) => value + 1), 1000);
    return () => window.clearInterval(timer);
  }, []);

  const visible = useMemo(
    () => [...jobs]
      .filter((job) => showAll || !projectId || job.projectId === projectId)
      .sort((left, right) => Date.parse(right.updatedUtc) - Date.parse(left.updatedUtc)),
    [jobs, projectId, showAll],
  );

  async function run(action: () => Promise<unknown>, success: string) {
    if (busy) return;
    setBusy(true);
    setMessage("");
    try {
      await action();
      setMessage(success);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Queue action failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="generation-queue" aria-labelledby="queue-heading">
      <div className="section-heading queue-heading">
        <div><p className="eyebrow">Persistent generation</p><h2 id="queue-heading">Generation Queue</h2><p>Live job state comes from server-sent events; DuckDB remains the authoritative source of truth.</p></div>
        <span className={`queue-live ${connected ? "is-connected" : ""}`}><i />{connected ? "Live" : "Reconnecting"}</span>
      </div>
      {message ? <div className="status-banner" role="status">{message}</div> : null}

      <div className="queue-toolbar">
        <label className="toggle-row"><input type="checkbox" checked={showAll} onChange={(event) => setShowAll(event.target.checked)} /><span>Show jobs from all projects</span></label>
        {projectId ? <div className="queue-scope-actions"><button className="button" type="button" disabled={busy} onClick={() => void run(() => pauseProjectJobs(projectId), "Project jobs paused where possible.")}>Pause project</button><button className="button" type="button" disabled={busy} onClick={() => void run(() => resumeProjectJobs(projectId), "Project jobs resumed/retried where possible.")}>Resume project</button><button className="button button-danger" type="button" disabled={busy} onClick={() => void run(() => cancelProjectJobs(projectId), "Project jobs cancelled where possible.")}>Cancel project jobs</button></div> : null}
      </div>

      {visible.length === 0 ? <div className="queue-empty">No generation jobs in this scope.</div> : (
        <div className="queue-list">
          {visible.map((job) => (
            <article className="queue-job" key={job.id}>
              <div className="queue-job-main">
                <div className="queue-job-title"><strong>{friendlyType(job.type)}</strong><span className={`job-state state-${job.state.toLowerCase()}`}>{job.state}</span></div>
                <span>{job.sceneId ? `Scene ${job.sceneId.slice(0, 8)}` : "Project/global job"} · elapsed {elapsed(job)}</span>
                {advanced ? <span>{job.providerId ?? "automatic"} · {job.modelId ?? "model pending"}</span> : <span>Automatic provider routing</span>}
                <span>attempt {job.attemptCount} · retries {job.retryCount}/{job.maxRetries} · {formatCost(job)}</span>
                {job.nextRunUtc ? <span>next run {new Date(job.nextRunUtc).toLocaleTimeString()}</span> : null}
                {job.errorMessage ? <span className="queue-error">{job.errorCode ? `${job.errorCode}: ` : ""}{job.errorMessage}</span> : null}
              </div>
              <div className="queue-job-actions">
                {!terminal.has(job.state) && job.state !== "Paused" ? <button className="button" type="button" disabled={busy} onClick={() => void run(() => pauseJob(job.id), "Job paused.")}>Pause</button> : null}
                {job.state === "Paused" ? <button className="button" type="button" disabled={busy} onClick={() => void run(() => resumeJob(job.id), "Job resumed.")}>Resume</button> : null}
                {retryable.has(job.state) ? <button className="button" type="button" disabled={busy} onClick={() => void run(() => retryJob(job.id), "Job retry requested.")}>Retry</button> : null}
                {terminal.has(job.state) || job.state === "FailedRetryable" ? <button className="button" type="button" disabled={busy} onClick={() => void run(() => restartJob(job.id), "Job restarted from persisted definition.")}>Restart</button> : null}
                {!terminal.has(job.state) ? <button className="button button-danger" type="button" disabled={busy} onClick={() => void run(() => cancelJob(job.id), "Job cancelled.")}>Cancel</button> : null}
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}

function friendlyType(type: string): string {
  if (type === "keyframe.image.generate") return "Keyframe image";
  if (type === "scene.video.generate") return "Scene animation";
  return type;
}

function elapsed(job: JobResponse): string {
  const start = Date.parse(job.startedUtc ?? job.createdUtc);
  const end = job.completedUtc ? Date.parse(job.completedUtc) : Date.now();
  const seconds = Math.max(0, Math.floor((end - start) / 1000));
  const minutes = Math.floor(seconds / 60);
  return `${minutes}:${String(seconds % 60).padStart(2, "0")}`;
}

function formatCost(job: JobResponse): string {
  const cost = job.actualCost ?? job.estimatedCost;
  return cost == null ? "cost pending" : `${job.actualCost == null ? "est. " : ""}${cost.toFixed(2)} ${job.currency ?? "USD"}`;
}
