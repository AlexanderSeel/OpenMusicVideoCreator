"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  getStoryboard,
  listPromptHistory,
  regenerateScenePrompt,
  type PromptVersionResponse,
  type StoryboardResponse,
  type StoryboardSceneResponse,
} from "@/src/api/client";
import { getClipPreviewUrl, listClipVariants, type ClipVariantResponse } from "@/src/api/clips";
import {
  initializeTimeline,
  listTimelineVersions,
  reorderTimelineClips,
  replaceTimelineClip,
  resetTimeline,
  restoreTimelineVersion,
  splitTimelineClip,
  updateTimelineClip,
  type ProjectTimelineVersion,
  type TimelineClip,
  type TimelineClipEdit,
  type TimelineTransitionKind,
} from "@/src/api/timeline";
import { TimelineCompositionControls } from "./TimelineCompositionControls";

interface AdvancedTimelineEditorProps {
  projectId?: string;
}

export function AdvancedTimelineEditor({ projectId }: AdvancedTimelineEditorProps) {
  const [timeline, setTimeline] = useState<ProjectTimelineVersion | null>(null);
  const [versions, setVersions] = useState<ProjectTimelineVersion[]>([]);
  const [storyboard, setStoryboard] = useState<StoryboardResponse | null>(null);
  const [selectedClipId, setSelectedClipId] = useState<string | null>(null);
  const [clipDraft, setClipDraft] = useState<TimelineClipEdit | null>(null);
  const [variants, setVariants] = useState<ClipVariantResponse[]>([]);
  const [promptHistory, setPromptHistory] = useState<PromptVersionResponse[]>([]);
  const [promptNotes, setPromptNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  const selectedClip = useMemo(
    () => timeline?.clips.find((clip) => clip.id === selectedClipId) ?? null,
    [selectedClipId, timeline],
  );
  const selectedScene = useMemo(
    () => storyboard?.scenes.find((scene) => scene.id === selectedClip?.sceneId) ?? null,
    [selectedClip, storyboard],
  );
  const duration = useMemo(
    () => timeline?.clips.reduce((maximum, clip) => Math.max(maximum, clip.timelineStartSeconds + clip.timelineDurationSeconds), 0) ?? 0,
    [timeline],
  );

  const load = useCallback(async (signal?: AbortSignal) => {
    if (!projectId) {
      setTimeline(null);
      setVersions([]);
      setStoryboard(null);
      return;
    }
    try {
      const [nextTimeline, nextStoryboard] = await Promise.all([
        initializeTimeline(projectId, signal),
        getStoryboard(projectId, signal),
      ]);
      setTimeline(nextTimeline);
      setStoryboard(nextStoryboard);
      setSelectedClipId((current) => current && nextTimeline.clips.some((clip) => clip.id === current) ? current : nextTimeline.clips[0]?.id ?? null);
      setVersions(await listTimelineVersions(projectId, signal));
    } catch (error) {
      if (!signal?.aborted) setMessage(error instanceof Error ? error.message : "Could not initialize Advanced timeline.");
    }
  }, [projectId]);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  useEffect(() => {
    if (!selectedClip) {
      setClipDraft(null);
      setVariants([]);
      setPromptHistory([]);
      return;
    }
    setClipDraft(editFromClip(selectedClip));
    if (!projectId) return;
    const controller = new AbortController();
    Promise.all([
      listClipVariants(projectId, selectedClip.sceneId, controller.signal),
      listPromptHistory(projectId, selectedClip.sceneId, controller.signal),
    ]).then(([nextVariants, prompts]) => {
      setVariants(nextVariants);
      setPromptHistory(prompts);
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setMessage(error instanceof Error ? error.message : "Could not load scene inspector data.");
    });
    return () => controller.abort();
  }, [projectId, selectedClip]);

  if (!projectId) {
    return (
      <section className="advanced-timeline advanced-timeline-empty" aria-labelledby="advanced-timeline-heading">
        <TimelineHeading />
        <p>Save the project before opening the Advanced Editor.</p>
      </section>
    );
  }

  if (!timeline) {
    return (
      <section className="advanced-timeline" aria-labelledby="advanced-timeline-heading">
        <TimelineHeading />
        {message ? <div className="status-banner" role="status">{message}</div> : <p>Preparing the selected scene timeline…</p>}
      </section>
    );
  }

  async function saveClip() {
    if (!projectId || !selectedClip || !clipDraft || busy) return;
    setBusy(true);
    setMessage("");
    try {
      const saved = await updateTimelineClip(projectId, selectedClip.id, clipDraft);
      acceptTimeline(saved, `Timeline saved as v${saved.version}. Previous versions remain recoverable.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not save timeline clip.");
    } finally {
      setBusy(false);
    }
  }

  async function moveClip(delta: number) {
    if (!projectId || !selectedClip || busy) return;
    const ordered = [...timeline.clips].sort((left, right) => left.sequence - right.sequence);
    const index = ordered.findIndex((clip) => clip.id === selectedClip.id);
    const target = index + delta;
    if (target < 0 || target >= ordered.length) return;
    [ordered[index], ordered[target]] = [ordered[target], ordered[index]];
    setBusy(true);
    try {
      const saved = await reorderTimelineClips(projectId, ordered.map((clip) => clip.id));
      acceptTimeline(saved, `Clip moved in timeline v${saved.version}; original generated media was not changed.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not reorder timeline.");
    } finally {
      setBusy(false);
    }
  }

  async function splitSelectedClip() {
    if (!projectId || !selectedClip || busy || selectedClip.timelineDurationSeconds <= 0.25) return;
    setBusy(true);
    try {
      const saved = await splitTimelineClip(projectId, selectedClip.id, selectedClip.timelineDurationSeconds / 2);
      acceptTimeline(saved, `Clip split non-destructively in timeline v${saved.version}.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not split clip.");
    } finally {
      setBusy(false);
    }
  }

  async function replaceVariant(variantId: string) {
    if (!projectId || !selectedClip || busy || variantId === selectedClip.clipVariantId) return;
    setBusy(true);
    try {
      const saved = await replaceTimelineClip(projectId, selectedClip.id, variantId);
      acceptTimeline(saved, `Timeline v${saved.version} now references the selected existing scene variant.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not replace timeline clip.");
    } finally {
      setBusy(false);
    }
  }

  async function restore(versionId: string) {
    if (!projectId || busy || versionId === timeline.id) return;
    setBusy(true);
    try {
      const saved = await restoreTimelineVersion(projectId, versionId);
      acceptTimeline(saved, `Timeline v${saved.version} restored from an earlier version without overwriting history.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not restore timeline version.");
    } finally {
      setBusy(false);
    }
  }

  async function reset() {
    if (!projectId || busy) return;
    setBusy(true);
    try {
      const saved = await resetTimeline(projectId);
      acceptTimeline(saved, `Timeline v${saved.version} rebuilt from the current storyboard and selected clips.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not reset timeline.");
    } finally {
      setBusy(false);
    }
  }

  async function regeneratePromptOnly() {
    if (!projectId || !selectedScene || busy) return;
    setBusy(true);
    try {
      const result = await regenerateScenePrompt(projectId, selectedScene.id, promptNotes);
      setStoryboard(result.storyboard);
      setPromptHistory((current) => [result.prompt, ...current]);
      setPromptNotes("");
      setMessage(`Prompt v${result.prompt.version} created. No image/video generation job was started.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not regenerate prompt.");
    } finally {
      setBusy(false);
    }
  }

  function acceptTimeline(saved: ProjectTimelineVersion, notice: string) {
    setTimeline(saved);
    setSelectedClipId((current) => current && saved.clips.some((clip) => clip.id === current) ? current : saved.clips[0]?.id ?? null);
    setVersions((current) => [saved, ...current.filter((version) => version.id !== saved.id)].sort((left, right) => right.version - left.version));
    setMessage(notice);
  }

  const subtitles = timeline.subtitles ?? [];

  return (
    <section className="advanced-timeline" aria-labelledby="advanced-timeline-heading">
      <TimelineHeading />
      {message ? <div className="status-banner" role="status">{message}</div> : null}

      <div className="timeline-toolbar">
        <div className="timeline-version-control">
          <label><span>Version</span><select value={timeline.id} disabled={busy} onChange={(event) => void restore(event.target.value)}>{versions.map((version) => <option key={version.id} value={version.id}>v{version.version} · {new Date(version.createdUtc).toLocaleString()}</option>)}</select></label>
          <button className="button" type="button" disabled={busy} onClick={() => void reset()}>Reset from storyboard</button>
        </div>
        <div className="protected-music"><span aria-hidden="true">🔒</span><div><strong>Original music track protected</strong><span>Song asset {timeline.songMediaAssetId.slice(0, 8)}… cannot be replaced or destructively edited by this timeline.</span></div></div>
      </div>

      <div className="timeline-scroll" aria-label="Advanced project timeline">
        <TimelineRuler duration={duration} />
        <div className="timeline-lane music-lane"><span className="lane-label">Music</span><div className="lane-content"><div className="music-wave-placeholder" title="Protected original Song"><span>Original Song · {formatTime(duration)}</span></div></div></div>
        <div className="timeline-lane clip-lane"><span className="lane-label">Clips</span><div className="lane-content timeline-flex">{[...timeline.clips].sort((a, b) => a.sequence - b.sequence).map((clip) => <button key={clip.id} type="button" className={`timeline-clip ${clip.id === selectedClipId ? "is-selected" : ""}`} style={{ flexGrow: clip.timelineDurationSeconds }} onClick={() => setSelectedClipId(clip.id)}><strong>{clip.sequence.toString().padStart(2, "0")}</strong><span>{formatTime(clip.timelineStartSeconds)} · {clip.timelineDurationSeconds.toFixed(1)}s</span></button>)}</div></div>
        <div className="timeline-lane transition-lane"><span className="lane-label">Transitions</span><div className="lane-content timeline-flex">{timeline.clips.map((clip) => <div key={clip.id} className="transition-chip" style={{ flexGrow: clip.timelineDurationSeconds }}><span>{clip.transitionIn}{clip.transitionDurationSeconds > 0 ? ` ${clip.transitionDurationSeconds.toFixed(2)}s` : ""}</span></div>)}</div></div>
        <div className="timeline-lane"><span className="lane-label">Overlays</span><div className="lane-content lane-absolute">{timeline.overlays.length === 0 ? <span className="lane-empty">No overlays</span> : timeline.overlays.map((overlay) => <span key={overlay.id} className="overlay-segment" style={segmentStyle(overlay.startSeconds, overlay.endSeconds, duration)}>Overlay · {Math.round(overlay.opacity * 100)}%</span>)}</div></div>
        <div className="timeline-lane"><span className="lane-label">Effects</span><div className="lane-content lane-absolute">{timeline.effects.length === 0 ? <span className="lane-empty">No effects</span> : timeline.effects.map((effect) => <span key={effect.id} className="effect-segment" style={segmentStyle(effect.startSeconds, effect.endSeconds, duration)}>{effect.kind} · {Math.round(effect.strength * 100)}%</span>)}</div></div>
        <div className="timeline-lane"><span className="lane-label">Subtitles</span><div className="lane-content lane-absolute">{subtitles.length === 0 ? <span className="lane-empty">No subtitles</span> : subtitles.map((subtitle) => <span key={subtitle.id} className="subtitle-segment" style={segmentStyle(subtitle.startSeconds, subtitle.endSeconds, duration)}>{subtitle.text}</span>)}</div></div>
      </div>

      <TimelineCompositionControls projectId={projectId} timeline={timeline} disabled={busy} onChanged={acceptTimeline} onError={(error) => setMessage(error)} />

      {selectedClip && clipDraft ? (
        <div className="advanced-inspector-layout">
          <div className="timeline-preview-card">
            <video controls preload="metadata" src={getClipPreviewUrl(projectId, selectedClip.sceneId, selectedClip.clipVariantId)} />
            <div className="timeline-clip-actions"><button className="button" type="button" disabled={busy || selectedClip.sequence === 1} onClick={() => void moveClip(-1)}>Move earlier</button><button className="button" type="button" disabled={busy || selectedClip.sequence === timeline.clips.length} onClick={() => void moveClip(1)}>Move later</button><button className="button" type="button" disabled={busy} onClick={() => void splitSelectedClip()}>Split at center</button></div>
          </div>
          <aside className="advanced-scene-inspector" aria-label="Advanced Scene Inspector">
            <header><div><p className="eyebrow">Scene Inspector</p><h3>Scene {selectedClip.sequence}</h3></div><span>timeline v{timeline.version}</span></header>
            <InspectorStory scene={selectedScene} />
            <InspectorCharacter scene={selectedScene} />
            <InspectorEnvironment scene={selectedScene} />
            <InspectorCamera scene={selectedScene} draft={clipDraft} patch={patchDraft} />
            <section className="inspector-section"><h4>Generation</h4><p className="inspector-help">Provider/model-specific generation settings stay in the capability-aware Generation workspace. This editor only references completed variants.</p><label className="field"><span>Existing scene variant</span><select value={selectedClip.clipVariantId} disabled={busy} onChange={(event) => void replaceVariant(event.target.value)}>{variants.filter((variant) => variant.state === "Completed" && variant.mediaAssetId).map((variant) => <option key={variant.id} value={variant.id}>Variant {variant.variantNumber}{variant.isSelected ? " · scene selected" : ""}{variant.providerId ? ` · ${variant.providerId}/${variant.modelId}` : ""}</option>)}</select></label><div className="field-grid two-columns"><NumberField label="Source in (s)" value={clipDraft.sourceInSeconds} min={0} step={0.05} onChange={(value) => patchDraft({ sourceInSeconds: value })} /><NumberField label="Source duration (s)" value={clipDraft.sourceDurationSeconds} min={0.05} step={0.05} onChange={(value) => patchDraft({ sourceDurationSeconds: value })} /><NumberField label="Playback rate" value={clipDraft.playbackRate} min={0.5} max={2} step={0.05} onChange={(value) => patchDraft({ playbackRate: value })} /><NumberField label="Freeze extension (s)" value={clipDraft.freezeExtensionSeconds} min={0} step={0.05} onChange={(value) => patchDraft({ freezeExtensionSeconds: value })} /></div><div className="field-grid two-columns"><label className="field"><span>Transition in</span><select value={clipDraft.transitionIn} onChange={(event) => patchDraft({ transitionIn: event.target.value as TimelineTransitionKind, transitionDurationSeconds: event.target.value === "Cut" ? 0 : clipDraft.transitionDurationSeconds || 0.35 })}><option>Cut</option><option>Fade</option><option>Crossfade</option></select></label><NumberField label="Transition duration" value={clipDraft.transitionDurationSeconds} min={0} max={Math.min(2, selectedClip.timelineDurationSeconds / 2)} step={0.05} disabled={clipDraft.transitionIn === "Cut"} onChange={(value) => patchDraft({ transitionDurationSeconds: value })} /></div><button className="button button-primary" type="button" disabled={busy} onClick={() => void saveClip()}>Save as new timeline version</button></section>
            <section className="inspector-section"><h4>Prompt</h4><p className="inspector-help">Prompt edits remain separate from paid generation.</p><label className="field"><span>Refinement notes</span><textarea rows={2} value={promptNotes} onChange={(event) => setPromptNotes(event.target.value)} /></label><button className="button" type="button" disabled={busy || !selectedScene} onClick={() => void regeneratePromptOnly()}>Regenerate prompt only</button>{promptHistory.slice(0, 4).map((prompt) => <details key={prompt.id}><summary>Prompt v{prompt.version}</summary><p>{prompt.directorIntent}</p><pre>{prompt.finalProviderPrompt}</pre></details>)}</section>
          </aside>
        </div>
      ) : null}
    </section>
  );

  function patchDraft(patch: Partial<TimelineClipEdit>) {
    setClipDraft((current) => current ? { ...current, ...patch } : current);
  }
}

function TimelineHeading() {
  return <div className="section-heading"><div><span>11</span><h2 id="advanced-timeline-heading">Advanced Editor</h2></div><p>Versioned timeline edits; generated media and the original music track remain non-destructive.</p></div>;
}

function InspectorStory({ scene }: { scene: StoryboardSceneResponse | null }) {
  return <section className="inspector-section"><h4>Story</h4>{scene ? <><p><strong>{scene.details.purpose}</strong></p><p>{scene.details.associatedLyric || "No associated lyric"}</p><p>{scene.action}</p></> : <p className="inspector-help">This split segment retains its source scene provenance.</p>}</section>;
}

function InspectorCharacter({ scene }: { scene: StoryboardSceneResponse | null }) {
  return <section className="inspector-section"><h4>Character</h4>{scene ? <><p>{scene.characterIds.length ? `${scene.characterIds.length} referenced character(s)` : "No character reference"}</p><p>{scene.details.emotion}</p><p>{scene.details.continuityRequirements}</p></> : null}</section>;
}

function InspectorEnvironment({ scene }: { scene: StoryboardSceneResponse | null }) {
  return <section className="inspector-section"><h4>Environment</h4>{scene ? <><p>{scene.environment}</p><p>{scene.details.lighting}</p><p>{scene.details.environmentMotion}</p></> : null}</section>;
}

function InspectorCamera({ scene, draft, patch }: { scene: StoryboardSceneResponse | null; draft: TimelineClipEdit; patch: (patch: Partial<TimelineClipEdit>) => void }) {
  const transform = draft.transform;
  const color = draft.color;
  return <section className="inspector-section"><h4>Camera</h4>{scene ? <><p>{scene.camera}</p><p>{scene.details.composition}</p></> : null}<div className="field-grid two-columns"><NumberField label="Scale" value={transform.scale} min={0.25} max={4} step={0.05} onChange={(value) => patch({ transform: { ...transform, scale: value } })} /><NumberField label="Opacity" value={transform.opacity} min={0} max={1} step={0.05} onChange={(value) => patch({ transform: { ...transform, opacity: value } })} /><NumberField label="Position X" value={transform.positionX} min={-1} max={1} step={0.05} onChange={(value) => patch({ transform: { ...transform, positionX: value } })} /><NumberField label="Position Y" value={transform.positionY} min={-1} max={1} step={0.05} onChange={(value) => patch({ transform: { ...transform, positionY: value } })} /><NumberField label="Brightness" value={color.brightness} min={-1} max={1} step={0.05} onChange={(value) => patch({ color: { ...color, brightness: value } })} /><NumberField label="Contrast" value={color.contrast} min={0} max={2} step={0.05} onChange={(value) => patch({ color: { ...color, contrast: value } })} /><NumberField label="Saturation" value={color.saturation} min={0} max={3} step={0.05} onChange={(value) => patch({ color: { ...color, saturation: value } })} /></div><details><summary>Crop</summary><div className="field-grid two-columns"><NumberField label="Left" value={transform.cropLeft} min={0} max={0.9} step={0.01} onChange={(value) => patch({ transform: { ...transform, cropLeft: value } })} /><NumberField label="Right" value={transform.cropRight} min={0} max={0.9} step={0.01} onChange={(value) => patch({ transform: { ...transform, cropRight: value } })} /><NumberField label="Top" value={transform.cropTop} min={0} max={0.9} step={0.01} onChange={(value) => patch({ transform: { ...transform, cropTop: value } })} /><NumberField label="Bottom" value={transform.cropBottom} min={0} max={0.9} step={0.01} onChange={(value) => patch({ transform: { ...transform, cropBottom: value } })} /></div></details></section>;
}

function NumberField({ label, value, onChange, min, max, step, disabled }: { label: string; value: number; onChange: (value: number) => void; min?: number; max?: number; step?: number; disabled?: boolean }) {
  return <label className="field"><span>{label}</span><input type="number" value={Number.isFinite(value) ? value : 0} min={min} max={max} step={step ?? 0.1} disabled={disabled} onChange={(event) => onChange(Number(event.target.value))} /></label>;
}

function editFromClip(clip: TimelineClip): TimelineClipEdit {
  return {
    sourceInSeconds: clip.sourceInSeconds,
    sourceDurationSeconds: clip.sourceDurationSeconds,
    playbackRate: clip.playbackRate,
    freezeExtensionSeconds: clip.freezeExtensionSeconds,
    transitionIn: clip.transitionIn,
    transitionDurationSeconds: clip.transitionDurationSeconds,
    transform: { ...clip.transform },
    color: { ...clip.color },
  };
}

function formatTime(seconds: number): string {
  const safe = Math.max(0, Math.round(seconds));
  return `${Math.floor(safe / 60)}:${String(safe % 60).padStart(2, "0")}`;
}

function segmentStyle(start: number, end: number, duration: number) {
  return {
    left: `${duration > 0 ? (start / duration) * 100 : 0}%`,
    width: `${duration > 0 ? ((end - start) / duration) * 100 : 0}%`,
  };
}

function TimelineRuler({ duration }: { duration: number }) {
  const marks = Array.from({ length: Math.max(2, Math.ceil(duration / 15) + 1) }, (_, index) => Math.min(duration, index * 15));
  return <div className="timeline-ruler"><span className="lane-label">Time</span><div className="lane-content">{marks.map((seconds) => <span key={seconds} style={{ left: `${duration > 0 ? (seconds / duration) * 100 : 0}%` }}>{formatTime(seconds)}</span>)}</div></div>;
}
