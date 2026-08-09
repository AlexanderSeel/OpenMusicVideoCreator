"use client";

import { useEffect, useMemo, useState } from "react";
import {
  getStoryboard,
  getVisualArc,
  listPromptHistory,
  planStoryboard,
  regenerateScenePrompt,
  reorderStoryboard,
  saveVisualArc,
  updateStoryboardScene,
  type DirectorControls,
  type PromptVersionResponse,
  type SceneUpdateRequest,
  type StoryboardResponse,
  type StoryboardSceneResponse,
  type VisualArcResponse,
  type VisualLibraryResponse,
} from "@/src/api/client";

interface DirectorStoryboardPanelProps {
  projectId?: string;
  visualLibrary: VisualLibraryResponse[];
}

const defaultControls: DirectorControls = {
  literalToSymbolic: 0.55,
  narrativeStrength: 0.65,
  abstraction: 0.45,
  emotion: 0.7,
  darkness: 0.4,
  surrealism: 0.4,
  complexity: 0.55,
  actingIntensity: 0.55,
  cameraEnergy: 0.55,
};

const controlDefinitions: Array<{ key: keyof DirectorControls; label: string; low: string; high: string }> = [
  { key: "literalToSymbolic", label: "Literal ↔ symbolic", low: "literal", high: "symbolic" },
  { key: "narrativeStrength", label: "Narrative strength", low: "loose", high: "strong" },
  { key: "abstraction", label: "Abstraction", low: "concrete", high: "abstract" },
  { key: "emotion", label: "Emotion", low: "restrained", high: "intense" },
  { key: "darkness", label: "Darkness / warmth", low: "warm", high: "dark" },
  { key: "surrealism", label: "Surrealism / realism", low: "real", high: "surreal" },
  { key: "complexity", label: "Visual complexity", low: "minimal", high: "layered" },
  { key: "actingIntensity", label: "Acting", low: "subtle", high: "expressive" },
  { key: "cameraEnergy", label: "Camera energy", low: "still", high: "dynamic" },
];

export function DirectorStoryboardPanel({ projectId, visualLibrary }: DirectorStoryboardPanelProps) {
  const [controls, setControls] = useState<DirectorControls>(defaultControls);
  const [arc, setArc] = useState<VisualArcResponse | null>(null);
  const [storyboard, setStoryboard] = useState<StoryboardResponse | null>(null);
  const [selectedSceneId, setSelectedSceneId] = useState<string | null>(null);
  const [sceneDraft, setSceneDraft] = useState<StoryboardSceneResponse | null>(null);
  const [promptHistory, setPromptHistory] = useState<PromptVersionResponse[]>([]);
  const [promptNotes, setPromptNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    if (!projectId) {
      setArc(null);
      setStoryboard(null);
      setSelectedSceneId(null);
      return () => controller.abort();
    }
    Promise.all([getVisualArc(projectId, controller.signal), getStoryboard(projectId, controller.signal)])
      .then(([nextArc, nextStoryboard]) => {
        setArc(nextArc);
        setStoryboard(nextStoryboard);
        if (nextArc) setControls(nextArc.controls);
        setSelectedSceneId(nextStoryboard?.scenes[0]?.id ?? null);
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setMessage(error instanceof Error ? error.message : "Could not load Director planning.");
      });
    return () => controller.abort();
  }, [projectId]);

  const selectedScene = useMemo(
    () => storyboard?.scenes.find((scene) => scene.id === selectedSceneId) ?? null,
    [storyboard, selectedSceneId],
  );

  useEffect(() => {
    setSceneDraft(selectedScene);
    const controller = new AbortController();
    if (!projectId || !selectedScene) {
      setPromptHistory([]);
      return () => controller.abort();
    }
    listPromptHistory(projectId, selectedScene.id, controller.signal)
      .then(setPromptHistory)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setMessage(error instanceof Error ? error.message : "Could not load prompt history.");
      });
    return () => controller.abort();
  }, [projectId, selectedScene]);

  async function runPlan() {
    if (!projectId || busy) return;
    setBusy(true);
    setMessage("");
    try {
      const result = await planStoryboard(projectId, controls);
      setArc(result.visualArc);
      setStoryboard(result.storyboard);
      setSelectedSceneId(result.storyboard.scenes[0]?.id ?? null);
      setMessage(`Director plan created: Visual Arc v${result.visualArc.version}, storyboard v${result.storyboard.version}.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Director planning failed.");
    } finally {
      setBusy(false);
    }
  }

  async function saveArcChanges() {
    if (!projectId || !arc || busy) return;
    setBusy(true);
    try {
      const saved = await saveVisualArc(projectId, { ...arc, controls });
      setArc(saved);
      setMessage(`Visual Arc saved as version ${saved.version}.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not save Visual Arc.");
    } finally {
      setBusy(false);
    }
  }

  async function saveSceneChanges() {
    if (!projectId || !sceneDraft || busy) return;
    setBusy(true);
    try {
      const request: SceneUpdateRequest = {
        startSeconds: sceneDraft.startSeconds,
        endSeconds: sceneDraft.endSeconds,
        title: sceneDraft.title,
        directorIntent: sceneDraft.directorIntent,
        action: sceneDraft.action,
        environment: sceneDraft.environment,
        camera: sceneDraft.camera,
        transitionIn: sceneDraft.transitionIn,
        characterIds: sceneDraft.characterIds,
        styleIds: sceneDraft.styleIds,
        locationIds: sceneDraft.locationIds,
      };
      const saved = await updateStoryboardScene(projectId, sceneDraft.id, request);
      setStoryboard(saved);
      setSelectedSceneId(sceneDraft.id);
      setMessage(`Scene saved as storyboard version ${saved.version}. A new prompt revision was created locally.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not save scene.");
    } finally {
      setBusy(false);
    }
  }

  async function moveScene(delta: number) {
    if (!projectId || !storyboard || !selectedScene || busy) return;
    const scenes = [...storyboard.scenes].sort((left, right) => left.sequence - right.sequence);
    const index = scenes.findIndex((scene) => scene.id === selectedScene.id);
    const target = index + delta;
    if (target < 0 || target >= scenes.length) return;
    [scenes[index], scenes[target]] = [scenes[target], scenes[index]];
    setBusy(true);
    try {
      const saved = await reorderStoryboard(projectId, scenes.map((scene) => scene.id));
      setStoryboard(saved);
      setMessage(`Storyboard reordered as version ${saved.version}; musical timing slots remain intact.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not reorder storyboard.");
    } finally {
      setBusy(false);
    }
  }

  async function regeneratePrompt() {
    if (!projectId || !selectedScene || busy) return;
    setBusy(true);
    try {
      const result = await regenerateScenePrompt(projectId, selectedScene.id, promptNotes);
      setStoryboard(result.storyboard);
      setPromptHistory((current) => [result.prompt, ...current]);
      setPromptNotes("");
      setMessage(`Prompt version ${result.prompt.version} created. No image/video generation was started.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not regenerate prompt.");
    } finally {
      setBusy(false);
    }
  }

  if (!projectId) {
    return (
      <section className="director-panel director-empty" aria-labelledby="director-heading">
        <DirectorHeading />
        <p>Save the project and analyze its song before Director planning.</p>
      </section>
    );
  }

  return (
    <section className="director-panel" aria-labelledby="director-heading">
      <DirectorHeading />
      {message ? <div className="status-banner" role="status">{message}</div> : null}

      <div className="director-controls">
        <div className="director-control-grid">
          {controlDefinitions.map((definition) => (
            <label key={definition.key} className="director-control">
              <span><strong>{definition.label}</strong><output>{Math.round(controls[definition.key] * 100)}%</output></span>
              <input type="range" min="0" max="1" step="0.05" value={controls[definition.key]} disabled={busy} onChange={(event) => setControls((current) => ({ ...current, [definition.key]: Number(event.target.value) }))} />
              <small><span>{definition.low}</span><span>{definition.high}</span></small>
            </label>
          ))}
        </div>
        <button className="button button-primary" type="button" disabled={busy} onClick={() => void runPlan()}>{busy ? "Working…" : storyboard ? "Replan storyboard" : "Create Director plan"}</button>
      </div>

      {arc ? (
        <div className="visual-arc-editor">
          <div className="structure-heading"><div><strong>Visual Arc · v{arc.version}</strong><span>Editable emotional/visual progression. Saving creates a new version.</span></div><button className="button" type="button" disabled={busy} onClick={() => void saveArcChanges()}>Save Visual Arc</button></div>
          <label className="field"><span>Arc summary</span><textarea rows={2} value={arc.summary} onChange={(event) => setArc((current) => current ? { ...current, summary: event.target.value } : current)} /></label>
          <div className="arc-points">
            {arc.points.map((point, index) => (
              <article className="arc-point" key={point.id}>
                <div className="arc-point-top"><input aria-label={`Arc point ${index + 1} label`} value={point.label} onChange={(event) => updateArcPoint(index, { label: event.target.value })} /><input aria-label={`Arc point ${index + 1} time`} type="number" min="0" step="0.1" value={point.timeSeconds} onChange={(event) => updateArcPoint(index, { timeSeconds: Number(event.target.value) })} /></div>
                <textarea aria-label={`Arc point ${index + 1} description`} rows={2} value={point.description} onChange={(event) => updateArcPoint(index, { description: event.target.value })} />
                <ArcSlider label="Emotion" value={point.emotionalIntensity} onChange={(value) => updateArcPoint(index, { emotionalIntensity: value })} />
                <ArcSlider label="Visual" value={point.visualIntensity} onChange={(value) => updateArcPoint(index, { visualIntensity: value })} />
                <ArcSlider label="Camera" value={point.cameraEnergy} onChange={(value) => updateArcPoint(index, { cameraEnergy: value })} />
              </article>
            ))}
          </div>
        </div>
      ) : null}

      {storyboard ? (
        <div className="storyboard-workspace">
          <div className="structure-heading"><div><strong>Storyboard · v{storyboard.version}</strong><span>{storyboard.scenes.length} scenes aligned to the analyzed music structure.</span></div></div>
          <div className="storyboard-layout">
            <div className="storyboard-cards" role="list" aria-label="Storyboard scenes">
              {[...storyboard.scenes].sort((left, right) => left.sequence - right.sequence).map((scene) => (
                <button key={scene.id} role="listitem" type="button" className={`storyboard-card ${scene.id === selectedSceneId ? "is-selected" : ""}`} onClick={() => setSelectedSceneId(scene.id)}>
                  <span className="scene-number">{String(scene.sequence).padStart(2, "0")}</span>
                  <strong>{scene.title}</strong>
                  <span>{formatTime(scene.startSeconds)} – {formatTime(scene.endSeconds)} · {(scene.endSeconds - scene.startSeconds).toFixed(1)}s</span>
                  <small>{scene.directorIntent}</small>
                </button>
              ))}
            </div>

            {sceneDraft ? (
              <aside className="scene-inspector" aria-label="Selected scene editor">
                <div className="scene-inspector-heading"><div><strong>Scene {sceneDraft.sequence}</strong><span>Edits version only this storyboard state.</span></div><div><button className="icon-button" type="button" disabled={busy || sceneDraft.sequence === 1} onClick={() => void moveScene(-1)} aria-label="Move scene earlier">↑</button><button className="icon-button" type="button" disabled={busy || sceneDraft.sequence === storyboard.scenes.length} onClick={() => void moveScene(1)} aria-label="Move scene later">↓</button></div></div>
                <label className="field"><span>Title</span><input value={sceneDraft.title} onChange={(event) => patchScene({ title: event.target.value })} /></label>
                <div className="field-grid two-columns"><label className="field"><span>Start</span><input type="number" step="0.1" min="0" value={sceneDraft.startSeconds} onChange={(event) => patchScene({ startSeconds: Number(event.target.value) })} /></label><label className="field"><span>End</span><input type="number" step="0.1" min="0" value={sceneDraft.endSeconds} onChange={(event) => patchScene({ endSeconds: Number(event.target.value) })} /></label></div>
                <label className="field"><span>Director Intent</span><textarea rows={4} value={sceneDraft.directorIntent} onChange={(event) => patchScene({ directorIntent: event.target.value })} /></label>
                <label className="field"><span>Action</span><textarea rows={3} value={sceneDraft.action} onChange={(event) => patchScene({ action: event.target.value })} /></label>
                <label className="field"><span>Environment</span><textarea rows={3} value={sceneDraft.environment} onChange={(event) => patchScene({ environment: event.target.value })} /></label>
                <label className="field"><span>Camera</span><textarea rows={2} value={sceneDraft.camera} onChange={(event) => patchScene({ camera: event.target.value })} /></label>
                <label className="field"><span>Transition in</span><input value={sceneDraft.transitionIn} onChange={(event) => patchScene({ transitionIn: event.target.value })} /></label>
                <ReferenceChips title="Characters" ids={sceneDraft.characterIds} library={visualLibrary} />
                <ReferenceChips title="Styles" ids={sceneDraft.styleIds} library={visualLibrary} />
                <ReferenceChips title="Locations" ids={sceneDraft.locationIds} library={visualLibrary} />
                <button className="button button-primary" type="button" disabled={busy} onClick={() => void saveSceneChanges()}>Save scene</button>

                <div className="prompt-history">
                  <div className="prompt-history-heading"><strong>Prompt history</strong><span>Prompt revision does not spend generation credits.</span></div>
                  <label className="field"><span>Refinement notes</span><textarea rows={2} value={promptNotes} onChange={(event) => setPromptNotes(event.target.value)} placeholder="More intimate camera, less literal, preserve wardrobe…" /></label>
                  <button className="button" type="button" disabled={busy} onClick={() => void regeneratePrompt()}>Regenerate prompt only</button>
                  {promptHistory.map((prompt) => (
                    <details className="prompt-version" key={prompt.id} open={prompt.id === sceneDraft.selectedPromptVersionId}>
                      <summary>Prompt v{prompt.version} · template {prompt.templateName} v{prompt.templateVersion}</summary>
                      <div><strong>Director Intent</strong><p>{prompt.directorIntent}</p><strong>Final Provider Prompt</strong><pre>{prompt.finalProviderPrompt}</pre></div>
                    </details>
                  ))}
                </div>
              </aside>
            ) : null}
          </div>
        </div>
      ) : (
        <div className="director-callout"><strong>No storyboard yet</strong><span>Analyze the song first, then create a Director plan. The mock planner requires no paid provider.</span></div>
      )}
    </section>
  );

  function updateArcPoint(index: number, patch: Partial<VisualArcResponse["points"][number]>) {
    setArc((current) => current ? { ...current, points: current.points.map((point, currentIndex) => currentIndex === index ? { ...point, ...patch } : point) } : current);
  }

  function patchScene(patch: Partial<StoryboardSceneResponse>) {
    setSceneDraft((current) => current ? { ...current, ...patch } : current);
  }
}

function DirectorHeading() {
  return <div className="section-heading"><div><span>07</span><h2 id="director-heading">AI Director & storyboard</h2></div><p>Music-aware planning first; generation stays a later explicit step.</p></div>;
}

function ArcSlider({ label, value, onChange }: { label: string; value: number; onChange: (value: number) => void }) {
  return <label className="arc-slider"><span>{label}<output>{Math.round(value * 100)}%</output></span><input type="range" min="0" max="1" step="0.05" value={value} onChange={(event) => onChange(Number(event.target.value))} /></label>;
}

function ReferenceChips({ title, ids, library }: { title: string; ids: string[]; library: VisualLibraryResponse[] }) {
  if (ids.length === 0) return null;
  return <div className="scene-reference-chips"><span>{title}</span><div>{ids.map((id) => <em key={id}>{library.find((item) => item.id === id)?.name ?? "Missing reference"}</em>)}</div></div>;
}

function formatTime(seconds: number): string {
  const safe = Math.max(0, Math.round(seconds));
  return `${Math.floor(safe / 60)}:${String(safe % 60).padStart(2, "0")}`;
}
