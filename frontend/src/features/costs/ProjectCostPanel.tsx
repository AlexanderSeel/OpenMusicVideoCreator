"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { getProjectCosts, type ProjectCostSummary } from "@/src/api/costs";
import { jobEventsUrl } from "@/src/api/jobs";
import type { StudioMode } from "@/src/features/generation/KeyframeWorkspace";

interface ProjectCostPanelProps {
  projectId?: string;
  mode: StudioMode;
}

export function ProjectCostPanel({ projectId, mode }: ProjectCostPanelProps) {
  const [summary, setSummary] = useState<ProjectCostSummary | null>(null);
  const [message, setMessage] = useState("");

  const refresh = useCallback(async (signal?: AbortSignal) => {
    if (!projectId) {
      setSummary(null);
      return;
    }
    try {
      setSummary(await getProjectCosts(projectId, signal));
      setMessage("");
    } catch (error) {
      if (!signal?.aborted) setMessage(error instanceof Error ? error.message : "Could not load project costs.");
    }
  }, [projectId]);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);
    if (!projectId) return () => controller.abort();

    const events = new EventSource(jobEventsUrl());
    const reload = () => void refresh();
    events.addEventListener("ready", reload);
    events.addEventListener("job", reload);
    return () => {
      controller.abort();
      events.close();
    };
  }, [projectId, refresh]);

  const utilization = useMemo(() => {
    if (!summary?.maximumBudget || summary.maximumBudget <= 0) return null;
    return Math.min(1, summary.projectedCost / summary.maximumBudget);
  }, [summary]);

  if (!projectId) {
    return (
      <section className="cost-panel cost-panel-empty" aria-labelledby="cost-heading">
        <CostHeading />
        <p>Save a project before tracking generation cost and budget.</p>
      </section>
    );
  }

  return (
    <section className="cost-panel" aria-labelledby="cost-heading">
      <CostHeading />
      {message ? <div className="status-banner" role="status">{message}</div> : null}
      {summary ? (
        <>
          <div className="cost-stats" aria-label="Project generation cost summary">
            <CostStat label="Actual spend" value={money(summary.actualCost, summary.currency)} />
            <CostStat label="Reserved" value={money(summary.reservedEstimatedCost, summary.currency)} help="Estimated cost for persisted jobs that do not have an actual cost yet." />
            <CostStat label="Projected" value={money(summary.projectedCost, summary.currency)} />
            <CostStat label="Remaining hard cap" value={summary.remainingBudget == null ? "No hard cap" : money(summary.remainingBudget, summary.currency)} />
          </div>

          {summary.maximumBudget != null ? (
            <div className="budget-meter">
              <div><span>Hard maximum</span><strong>{money(summary.maximumBudget, summary.currency)}</strong></div>
              <progress max={1} value={utilization ?? 0} aria-label="Projected budget utilization" />
              <small>{Math.round((utilization ?? 0) * 100)}% projected utilization{summary.estimatedBudget != null ? ` · planning target ${money(summary.estimatedBudget, summary.currency)}` : ""}</small>
            </div>
          ) : summary.estimatedBudget != null ? (
            <p className="cost-note">Planning budget: <strong>{money(summary.estimatedBudget, summary.currency)}</strong>. No hard maximum is configured.</p>
          ) : null}

          {summary.unknownCostJobCount > 0 ? (
            <div className="cost-warning" role="status">
              <strong>{summary.unknownCostJobCount} generation job{summary.unknownCostJobCount === 1 ? " has" : "s have"} unknown cost.</strong>
              <span>A configured hard cap blocks additional paid work until those costs are resolved. Zero-cost local/mock work is still allowed.</span>
            </div>
          ) : null}

          {mode !== "Simple" ? (
            <div className="cost-detail-grid">
              <CostTable
                title="Provider / model"
                headers={["Provider / model", "Actual", "Reserved", "Jobs"]}
                rows={summary.providers.map((item) => [
                  `${item.providerId ?? "local"}${item.modelId ? ` / ${item.modelId}` : ""}`,
                  money(item.actualCost, summary.currency),
                  money(item.reservedEstimatedCost, summary.currency),
                  item.jobCount.toString(),
                ])}
              />
              <CostTable
                title="Scene"
                headers={["Scene", "Actual", "Reserved", "Jobs"]}
                rows={summary.scenes.map((item) => [
                  item.sceneId ? `${item.sceneId.slice(0, 8)}…` : "Project-level",
                  money(item.actualCost, summary.currency),
                  money(item.reservedEstimatedCost, summary.currency),
                  item.jobCount.toString(),
                ])}
              />
            </div>
          ) : null}

          {mode !== "Simple" ? (
            <details className="generation-cost-history">
              <summary>Generation cost history ({summary.generations.length})</summary>
              <div className="generation-cost-list">
                {summary.generations.length === 0 ? <p>No billable generation jobs yet.</p> : summary.generations.map((item) => (
                  <article key={item.jobId}>
                    <div><strong>{item.type}</strong><span>{item.state} · {new Date(item.createdUtc).toLocaleString()}</span></div>
                    <div><span>{item.providerId ?? "local"}{item.modelId ? ` / ${item.modelId}` : ""}</span><strong>{item.actualCost > 0 ? money(item.actualCost, summary.currency) : `${money(item.reservedEstimatedCost, summary.currency)} reserved`}</strong></div>
                  </article>
                ))}
              </div>
            </details>
          ) : null}
        </>
      ) : null}
    </section>
  );
}

function CostHeading() {
  return <div className="section-heading"><div><span>10</span><h2 id="cost-heading">Cost & budget</h2></div><p>Persisted job costs drive spend visibility and hard-cap reservations before new generation is queued.</p></div>;
}

function CostStat({ label, value, help }: { label: string; value: string; help?: string }) {
  return <div className="cost-stat" title={help}><span>{label}</span><strong>{value}</strong>{help ? <small>{help}</small> : null}</div>;
}

function CostTable({ title, headers, rows }: { title: string; headers: string[]; rows: string[][] }) {
  return (
    <div className="cost-table-card">
      <strong>{title}</strong>
      <div className="cost-table" role="table" aria-label={`${title} cost breakdown`}>
        <div className="cost-table-row cost-table-head" role="row">{headers.map((header) => <span role="columnheader" key={header}>{header}</span>)}</div>
        {rows.length === 0 ? <p className="cost-empty-row">No cost records.</p> : rows.map((row, index) => <div className="cost-table-row" role="row" key={`${row[0]}-${index}`}>{row.map((value, cell) => <span role="cell" key={cell}>{value}</span>)}</div>)}
      </div>
    </div>
  );
}

function money(value: number, currency: string): string {
  return new Intl.NumberFormat(undefined, { style: "currency", currency, minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
}
