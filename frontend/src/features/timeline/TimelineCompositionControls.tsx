"use client";

import { useEffect, useMemo, useState } from "react";
import {
  deleteTimelineEffect,
  deleteTimelineOverlay,
  deleteTimelineSubtitle,
  upsertTimelineEffect,
  upsertTimelineOverlay,
  upsertTimelineSubtitle,
  type ProjectTimelineVersion,
  type TimelineEffectEdit,
  type TimelineEffectKind,
  type TimelineOverlayEdit,
  type TimelineSubtitleEdit,
} from "@/src/api/timeline";

interface TimelineCompositionControlsProps {
  projectId: string;
  timeline: ProjectTimelineVersion;
  disabled?: boolean;
  onChanged: (timeline: ProjectTimelineVersion, message: string) => void;
  onError: (message: string) => void;
}

const effectKinds: TimelineEffectKind[] = ["FadeToBlack", "Vignette", "Grayscale"];

export function TimelineCompositionControls({
  projectId,
  timeline,
  disabled = false,
  onChanged,
  onError,
}: TimelineCompositionControlsProps) {
  const [saving, setSaving] = useState(false);
  const [overlayDraft, setOverlayDraft] = useState<TimelineOverlayEdit>(() => newOverlay(timeline));
  const [effectDraft, setEffectDraft] = useState<TimelineEffectEdit>(() => newEffect(timeline));
  const [subtitleDraft, setSubtitleDraft] = useState<TimelineSubtitleEdit>(() => newSubtitle(timeline));
  const subtitles = useMemo(() => timeline.subtitles ?? [], [timeline.subtitles]);
  const busy = disabled || saving;

  useEffect(() => {
    setOverlayDraft(newOverlay(timeline));
    setEffectDraft(newEffect(timeline));
    setSubtitleDraft(newSubtitle(timeline));
    // Reset drafts only when the project changes. Timeline revisions retain the same duration.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId]);

  async function saveOverlay() {
    if (busy) return;
    setSaving(true);
    try {
      const saved = await upsertTimelineOverlay(projectId, overlayDraft);
      onChanged(saved, `Overlay saved in timeline v${saved.version}.`);
      setOverlayDraft(newOverlay(saved));
    } catch (error) {
      onError(error instanceof Error ? error.message : "Could not save overlay.");
    } finally {
      setSaving(false);
    }
  }

  async function removeOverlay(id: string) {
    if (busy) return;
    setSaving(true);
    try {
      const saved = await deleteTimelineOverlay(projectId, id);
      onChanged(saved, `Overlay removed in timeline v${saved.version}; prior versions remain recoverable.`);
      setOverlayDraft(newOverlay(saved));
    } catch (error) {
      onError(error instanceof Error ? error.message : "Could not remove overlay.");
    } finally {
      setSaving(false);
    }
  }

  async function saveEffect() {
    if (busy) return;
    setSaving(true);
    try {
      const saved = await upsertTimelineEffect(projectId, effectDraft);
      onChanged(saved, `Effect saved in timeline v${saved.version}.`);
      setEffectDraft(newEffect(saved));
    } catch (error) {
      onError(error instanceof Error ? error.message : "Could not save effect.");
    } finally {
      setSaving(false);
    }
  }

  async function removeEffect(id: string) {
    if (busy) return;
    setSaving(true);
    try {
      const saved = await deleteTimelineEffect(projectId, id);
      onChanged(saved, `Effect removed in timeline v${saved.version}; prior versions remain recoverable.`);
      setEffectDraft(newEffect(saved));
    } catch (error) {
      onError(error instanceof Error ? error.message : "Could not remove effect.");
    } finally {
      setSaving(false);
    }
  }

  async function saveSubtitle() {
    if (busy || !subtitleDraft.text.trim()) return;
    setSaving(true);
    try {
      const saved = await upsertTimelineSubtitle(projectId, subtitleDraft);
      onChanged(saved, `Subtitle saved in timeline v${saved.version}.`);
      setSubtitleDraft(newSubtitle(saved));
    } catch (error) {
      onError(error instanceof Error ? error.message : "Could not save subtitle.");
    } finally {
      setSaving(false);
    }
  }

  async function removeSubtitle(id: string) {
    if (busy) return;
    setSaving(true);
    try {
      const saved = await deleteTimelineSubtitle(projectId, id);
      onChanged(saved, `Subtitle removed in timeline v${saved.version}; prior versions remain recoverable.`);
      setSubtitleDraft(newSubtitle(saved));
    } catch (error) {
      onError(error instanceof Error ? error.message : "Could not remove subtitle.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="timeline-composition" aria-labelledby="timeline-composition-heading">
      <div className="structure-heading">
        <div><strong id="timeline-composition-heading">Composition lanes</strong><span>Overlay, effect, and subtitle changes always create a new timeline version.</span></div>
      </div>

      <div className="timeline-composition-grid">
        <article className="composition-card">
          <header><div><strong>Overlay</strong><span>Project-owned image/video media.</span></div>{overlayDraft.id ? <button className="button" type="button" disabled={busy} onClick={() => setOverlayDraft(newOverlay(timeline))}>New</button> : null}</header>
          <label className="field"><span>Project media asset ID</span><input value={overlayDraft.mediaAssetId} disabled={busy} onChange={(event) => setOverlayDraft((current) => ({ ...current, mediaAssetId: event.target.value }))} placeholder="UUID of project image/video asset" /></label>
          <div className="field-grid two-columns">
            <NumberField label="Start (s)" value={overlayDraft.startSeconds} min={0} max={timelineDuration(timeline)} step={0.05} disabled={busy} onChange={(value) => setOverlayDraft((current) => ({ ...current, startSeconds: value }))} />
            <NumberField label="End (s)" value={overlayDraft.endSeconds} min={0.05} max={timelineDuration(timeline)} step={0.05} disabled={busy} onChange={(value) => setOverlayDraft((current) => ({ ...current, endSeconds: value }))} />
            <NumberField label="Position X" value={overlayDraft.positionX} min={-1} max={1} step={0.05} disabled={busy} onChange={(value) => setOverlayDraft((current) => ({ ...current, positionX: value }))} />
            <NumberField label="Position Y" value={overlayDraft.positionY} min={-1} max={1} step={0.05} disabled={busy} onChange={(value) => setOverlayDraft((current) => ({ ...current, positionY: value }))} />
            <NumberField label="Scale" value={overlayDraft.scale} min={0.1} max={4} step={0.05} disabled={busy} onChange={(value) => setOverlayDraft((current) => ({ ...current, scale: value }))} />
            <NumberField label="Opacity" value={overlayDraft.opacity} min={0} max={1} step={0.05} disabled={busy} onChange={(value) => setOverlayDraft((current) => ({ ...current, opacity: value }))} />
          </div>
          <button className="button button-primary" type="button" disabled={busy || !overlayDraft.mediaAssetId.trim()} onClick={() => void saveOverlay()}>{overlayDraft.id ? "Update overlay" : "Add overlay"}</button>
          <CompositionList empty="No overlays" items={timeline.overlays.map((item) => ({ id: item.id, label: `${item.startSeconds.toFixed(1)}–${item.endSeconds.toFixed(1)}s · ${Math.round(item.opacity * 100)}%`, onEdit: () => setOverlayDraft({ ...item }), onDelete: () => void removeOverlay(item.id) }))} disabled={busy} />
        </article>

        <article className="composition-card">
          <header><div><strong>Effect</strong><span>Bounded lightweight video effects.</span></div>{effectDraft.id ? <button className="button" type="button" disabled={busy} onClick={() => setEffectDraft(newEffect(timeline))}>New</button> : null}</header>
          <label className="field"><span>Effect</span><select value={effectDraft.kind} disabled={busy} onChange={(event) => setEffectDraft((current) => ({ ...current, kind: event.target.value as TimelineEffectKind }))}>{effectKinds.map((kind) => <option key={kind}>{kind}</option>)}</select></label>
          <div className="field-grid two-columns">
            <NumberField label="Start (s)" value={effectDraft.startSeconds} min={0} max={timelineDuration(timeline)} step={0.05} disabled={busy} onChange={(value) => setEffectDraft((current) => ({ ...current, startSeconds: value }))} />
            <NumberField label="End (s)" value={effectDraft.endSeconds} min={0.05} max={timelineDuration(timeline)} step={0.05} disabled={busy} onChange={(value) => setEffectDraft((current) => ({ ...current, endSeconds: value }))} />
            <NumberField label="Strength" value={effectDraft.strength} min={0} max={1} step={0.05} disabled={busy} onChange={(value) => setEffectDraft((current) => ({ ...current, strength: value }))} />
          </div>
          <button className="button button-primary" type="button" disabled={busy} onClick={() => void saveEffect()}>{effectDraft.id ? "Update effect" : "Add effect"}</button>
          <CompositionList empty="No effects" items={timeline.effects.map((item) => ({ id: item.id, label: `${item.kind} · ${item.startSeconds.toFixed(1)}–${item.endSeconds.toFixed(1)}s`, onEdit: () => setEffectDraft({ ...item }), onDelete: () => void removeEffect(item.id) }))} disabled={busy} />
        </article>

        <article className="composition-card">
          <header><div><strong>Subtitle</strong><span>Burned into Preview/Final output and included in render provenance.</span></div>{subtitleDraft.id ? <button className="button" type="button" disabled={busy} onClick={() => setSubtitleDraft(newSubtitle(timeline))}>New</button> : null}</header>
          <label className="field"><span>Text</span><textarea rows={3} maxLength={500} value={subtitleDraft.text} disabled={busy} onChange={(event) => setSubtitleDraft((current) => ({ ...current, text: event.target.value }))} placeholder="Subtitle text" /></label>
          <div className="field-grid two-columns">
            <NumberField label="Start (s)" value={subtitleDraft.startSeconds} min={0} max={timelineDuration(timeline)} step={0.05} disabled={busy} onChange={(value) => setSubtitleDraft((current) => ({ ...current, startSeconds: value }))} />
            <NumberField label="End (s)" value={subtitleDraft.endSeconds} min={0.05} max={timelineDuration(timeline)} step={0.05} disabled={busy} onChange={(value) => setSubtitleDraft((current) => ({ ...current, endSeconds: value }))} />
            <NumberField label="Vertical position" value={subtitleDraft.positionY} min={-1} max={1} step={0.05} disabled={busy} onChange={(value) => setSubtitleDraft((current) => ({ ...current, positionY: value }))} />
            <NumberField label="Size" value={subtitleDraft.size} min={0.5} max={2} step={0.05} disabled={busy} onChange={(value) => setSubtitleDraft((current) => ({ ...current, size: value }))} />
            <NumberField label="Opacity" value={subtitleDraft.opacity} min={0} max={1} step={0.05} disabled={busy} onChange={(value) => setSubtitleDraft((current) => ({ ...current, opacity: value }))} />
          </div>
          <button className="button button-primary" type="button" disabled={busy || !subtitleDraft.text.trim()} onClick={() => void saveSubtitle()}>{subtitleDraft.id ? "Update subtitle" : "Add subtitle"}</button>
          <CompositionList empty="No subtitles" items={subtitles.map((item) => ({ id: item.id, label: `${item.startSeconds.toFixed(1)}–${item.endSeconds.toFixed(1)}s · ${item.text}`, onEdit: () => setSubtitleDraft({ ...item }), onDelete: () => void removeSubtitle(item.id) }))} disabled={busy} />
        </article>
      </div>
    </section>
  );
}

function CompositionList({ items, empty, disabled }: { items: Array<{ id: string; label: string; onEdit: () => void; onDelete: () => void }>; empty: string; disabled: boolean }) {
  if (items.length === 0) return <p className="composition-empty">{empty}</p>;
  return <ul className="composition-list">{items.map((item) => <li key={item.id}><span>{item.label}</span><div><button className="button" type="button" disabled={disabled} onClick={item.onEdit}>Edit</button><button className="button" type="button" disabled={disabled} onClick={item.onDelete}>Delete</button></div></li>)}</ul>;
}

function NumberField({ label, value, onChange, min, max, step, disabled }: { label: string; value: number; onChange: (value: number) => void; min?: number; max?: number; step?: number; disabled?: boolean }) {
  return <label className="field"><span>{label}</span><input type="number" value={Number.isFinite(value) ? value : 0} min={min} max={max} step={step ?? 0.1} disabled={disabled} onChange={(event) => onChange(Number(event.target.value))} /></label>;
}

function timelineDuration(timeline: ProjectTimelineVersion): number {
  return timeline.clips.reduce((maximum, clip) => Math.max(maximum, clip.timelineStartSeconds + clip.timelineDurationSeconds), 0);
}

function defaultEnd(timeline: ProjectTimelineVersion): number {
  return Math.min(timelineDuration(timeline), 3);
}

function newOverlay(timeline: ProjectTimelineVersion): TimelineOverlayEdit {
  return { id: null, mediaAssetId: "", startSeconds: 0, endSeconds: defaultEnd(timeline), positionX: 0, positionY: 0, scale: 1, opacity: 1 };
}

function newEffect(timeline: ProjectTimelineVersion): TimelineEffectEdit {
  return { id: null, kind: "FadeToBlack", startSeconds: 0, endSeconds: defaultEnd(timeline), strength: 0.5 };
}

function newSubtitle(timeline: ProjectTimelineVersion): TimelineSubtitleEdit {
  return { id: null, text: "", startSeconds: 0, endSeconds: defaultEnd(timeline), positionY: 0.8, size: 1, opacity: 1 };
}
