"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { getProviderCatalog, getStoryboard, type StoryboardResponse } from "@/src/api/client";
import {
  deleteClipVariant,
  generateSceneClip,
  getClipPreviewUrl,
  getVideoGenerationSettings,
  listClipVariants,
  saveVideoGenerationSettings,
  selectClipVariant,
  type ClipVariantResponse,
  type VideoGenerationSettingsRequest,
  type VideoGenerationSettingsResponse,
} from "@/src/api/clips";
import { getKeyframeApproval, type KeyframeApprovalStatusResponse } from "@/src/api/keyframes";
import type { StudioMode } from "./KeyframeWorkspace";

interface VideoGenerationWorkspaceProps {
  projectId?: string;
  mode: StudioMode;
}

type ProviderCatalog = Awaited<ReturnType<typeof getProviderCatalog>>;
type Provider = ProviderCatalog[number];
const activeStates = new Set(["Planned", "Queued", "Generating"]);

export function VideoGenerationWorkspace({ projectId, mode }: VideoGenerationWorkspaceProps) {
  const [storyboard, setStoryboard] = useState<StoryboardResponse | null>(null);
  const [sceneId, setSceneId] = useState("");
  const [variants, setVariants] = useState<ClipVariantResponse[]>([]);
  const [settings, setSettings] = useState<VideoGenerationSettingsResponse | null>(null);
  const [approval, setApproval] = useState<KeyframeApprovalStatusResponse | null>(null);
  const [providers, setProviders] = useState<ProviderCatalog>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const advanced = mode !== "Simple";

  const loadStoryboard = useCallback(async (signal?: AbortSignal) => {
    if (!projectId) {
      setStoryboard(null);
      setSceneId("");
      return;
    }
    const next = await getStoryboard(projectId, signal);
    setStoryboard(next);
    setSceneId((current) => next?.scenes.some((scene) => scene.id === current) ? current : next?.scenes[0]?.id ?? "");
  }, [projectId]);

  const loadSceneState = useCallback(async (currentSceneId: string, signal?: AbortSignal) => {
    if (!projectId || !currentSceneId) {
      setVariants([]);
      setSettings(null);
      setApproval(null);
      return;
    }
    const [nextVariants, nextSettings, nextApproval] = await Promise.all([
      listClipVariants(projectId, currentSceneId, signal),
      getVideoGenerationSettings(projectId, currentSceneId, signal),
      getKeyframeApproval(projectId, currentSceneId, signal),
    ]);
    setVariants(nextVariants);
    setSettings(nextSettings);
    setApproval(nextApproval);
  }, [projectId]);

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([loadStoryboard(controller.signal), getProviderCatalog(controller.signal).then(setProviders)])
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setMessage(error instanceof Error ? error.message : "Could not load video generation workspace.");
      });
    return () => controller.abort();
  }, [loadStoryboard]);

  useEffect(() => {
    const controller = new AbortController();
    loadSceneState(sceneId, controller.signal).catch((error: unknown) => {
      if (!controller.signal.aborted) setMessage(error instanceof Error ? error.message : "Could not load scene clip state.");
    });
    return () => controller.abort();
  }, [loadSceneState, sceneId]);

  const hasActive = variants.some((variant) => activeStates.has(variant.state));
  useEffect(() => {
    if (!hasActive || !sceneId) return;
    const timer = window.setInterval(() => void loadSceneState(sceneId).catch(() => undefined), 1500);
    return () => window.clearInterval(timer);
  }, [hasActive, loadSceneState, sceneId]);

  const videoProviders = useMemo(
    () => providers.filter((provider) => provider.settings.enabled && provider.models.some((model) => model.capabilities.includes("ImageToVideo"))),
    [providers],
  );
  const selectedProvider = useMemo(
    () => videoProviders.find((provider) => provider.id === settings?.providerId) ?? null,
    [videoProviders, settings?.providerId],
  );
  const selectedModel = useMemo(
    () => selectedProvider?.models.find((model) => model.modelId === settings?.modelId) ?? null,
    [selectedProvider, settings?.modelId],
  );
  const scene = storyboard?.scenes.find((candidate) => candidate.id === sceneId) ?? null;

  async function mutate(action: () => Promise<void>) {
    if (busy) return;
    setBusy(true);
    setMessage("");
    try {
      await action();
      if (sceneId) await loadSceneState(sceneId);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Video generation action failed.");
    } finally {
      setBusy(false);
    }
  }

  async function queueClip() {
    if (!projectId || !sceneId) return;
    await mutate(async () => {
      const result = await generateSceneClip(projectId, sceneId);
      setVariants((current) => [...current, result.variant]);
      setMessage("Scene animation queued. Existing selected clips remain unchanged.");
    });
  }

  async function saveSettings() {
    if (!projectId || !sceneId || !settings || !advanced) return;
    await mutate(async () => {
      const request: VideoGenerationSettingsRequest = {
        providerId: settings.providerId,
        modelId: settings.modelId,
        useEndFrame: settings.useEndFrame,
        resolution: settings.resolution,
        durationSeconds: settings.durationSeconds,
        allowFallback: settings.allowFallback,
      };
      setSettings(await saveVideoGenerationSettings(projectId, sceneId, request));
      setMessage("Scene video settings saved.");
    });
  }

  function patchSettings(patch: Partial<VideoGenerationSettingsResponse>) {
    setSettings((current) => current ? { ...current, ...patch } : current);
  }

  function chooseProvider(providerId: string) {
    if (!settings || !advanced) return;
    if (!providerId) {
      patchSettings({ providerId: null, modelId: null, resolution: null, durationSeconds: null });
      return;
    }
    const provider = videoProviders.find((candidate) => candidate.id === providerId);
    const model = provider?.models.find((candidate) => candidate.capabilities.includes("ImageToVideo"));
    patchSettings({
      providerId,
      modelId: model?.modelId ?? null,
      resolution: model?.supportedResolutions[0] ?? null,
      durationSeconds: model?.supportedDurationsSeconds[0] ?? null,
      useEndFrame: model?.supportsEndFrame ? settings.useEndFrame : false,
    });
  }

  function chooseModel(modelId: string) {
    if (!advanced) return;
    const model = selectedProvider?.models.find((candidate) => candidate.modelId === modelId) ?? null;
    patchSettings({
      modelId: model?.modelId ?? null,
      resolution: model?.supportedResolutions[0] ?? null,
      durationSeconds: model?.supportedDurationsSeconds[0] ?? null,
      useEndFrame: model?.supportsEndFrame ? settings?.useEndFrame ?? false : false,
    });
  }

  if (!projectId) {
    return <section className="video-workspace video-empty"><VideoHeading /><p>Save the project, create a storyboard, and approve keyframes before animation.</p></section>;
  }

  return (
    <section className="video-workspace" aria-labelledby="video-heading">
      <VideoHeading />
      {message ? <div className="status-banner" role="status">{message}</div> : null}
      {!storyboard ? <div className="video-callout"><strong>No storyboard available</strong><span>Create the Director storyboard and keyframes first.</span></div> : (
        <>
          <div className="video-toolbar">
            <label className="field"><span>Scene</span><select value={sceneId} onChange={(event) => setSceneId(event.target.value)}>{storyboard.scenes.map((item) => <option key={item.id} value={item.id}>Scene {item.sequence} · {item.title}</option>)}</select></label>
            <div><strong>{scene?.title}</strong><span>{scene ? `${scene.startSeconds.toFixed(1)}–${scene.endSeconds.toFixed(1)}s · ${scene.details.songSection}` : ""}</span></div>
            <button className="button button-primary" type="button" disabled={busy || !approval?.isApproved} onClick={() => void queueClip()}>{busy ? "Working…" : "Animate approved keyframes"}</button>
          </div>

          {!approval?.isApproved ? <div className="video-callout"><strong>Keyframe approval required</strong><span>Select and approve the current start/end keyframes above before creating a video job.</span></div> : null}

          {settings && advanced ? (
            <details className="video-settings">
              <summary>Advanced / Custom video settings</summary>
              <div className="video-settings-grid">
                <label className="field"><span>Provider routing</span><select value={settings.providerId ?? ""} onChange={(event) => chooseProvider(event.target.value)}><option value="">Automatic capability routing</option>{videoProviders.map((provider) => <option key={provider.id} value={provider.id}>{provider.displayName}</option>)}</select></label>
                <label className="field"><span>Model</span><select value={settings.modelId ?? ""} disabled={!selectedProvider} onChange={(event) => chooseModel(event.target.value)}><option value="">Automatic</option>{selectedProvider?.models.filter((model) => model.capabilities.includes("ImageToVideo")).map((model) => <option key={model.modelId} value={model.modelId}>{model.displayName}</option>)}</select></label>
                <label className="field"><span>Resolution</span><select value={settings.resolution ?? ""} disabled={!selectedModel || selectedModel.supportedResolutions.length === 0} onChange={(event) => patchSettings({ resolution: event.target.value || null })}><option value="">Project / automatic</option>{selectedModel?.supportedResolutions.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
                <label className="field"><span>Duration</span><select value={settings.durationSeconds ?? ""} disabled={!selectedModel || selectedModel.supportedDurationsSeconds.length === 0} onChange={(event) => patchSettings({ durationSeconds: event.target.value ? Number(event.target.value) : null })}><option value="">Scene / nearest supported</option>{selectedModel?.supportedDurationsSeconds.map((value) => <option key={value} value={value}>{value}s</option>)}</select></label>
                {selectedModel?.supportsEndFrame ? <label className="toggle-row"><input type="checkbox" checked={settings.useEndFrame} onChange={(event) => patchSettings({ useEndFrame: event.target.checked })} /><span>Use approved End keyframe</span></label> : null}
                {mode === "Custom" ? <label className="toggle-row"><input type="checkbox" checked={settings.allowFallback} onChange={(event) => patchSettings({ allowFallback: event.target.checked })} /><span>Allow automatic provider fallback</span></label> : null}
              </div>
              <button className="button" type="button" disabled={busy} onClick={() => void saveSettings()}>Save video settings</button>
            </details>
          ) : mode === "Simple" ? <div className="video-simple-note">Simple Mode automatically routes an image-to-video model and chooses the nearest supported duration/resolution. Provider-specific controls remain hidden.</div> : null}

          <div className="structure-heading"><div><strong>Animated clip variants</strong><span>Regeneration appends variants; successful selected media is never overwritten.</span></div><button className="button" type="button" disabled={busy || !approval?.isApproved} onClick={() => void queueClip()}>Regenerate scene</button></div>
          {variants.length === 0 ? <div className="video-empty-grid">No animated variants yet.</div> : (
            <div className="clip-grid">
              {variants.map((variant) => (
                <article className={`clip-card ${variant.isSelected ? "is-selected" : ""}`} key={variant.id}>
                  <div className="clip-preview">{variant.state === "Completed" ? <video controls muted preload="metadata" src={getClipPreviewUrl(projectId, sceneId, variant.id)} /> : <span>{variant.state}</span>}</div>
                  <div className="clip-meta"><div><strong>Variant {variant.variantNumber}</strong>{variant.isSelected ? <em>Selected</em> : null}</div><span>{variant.durationSeconds.toFixed(1)}s · {variant.aspectRatio} · {variant.resolution}</span><span>{advanced ? `${variant.providerId ?? "auto"} · ${variant.modelId ?? "pending"}` : "Automatic routing"}</span><span>{formatCost(variant)}</span></div>
                  <div className="clip-actions"><button className="button" type="button" disabled={busy || variant.state !== "Completed" || variant.isSelected} onClick={() => void mutate(async () => { await selectClipVariant(projectId, sceneId, variant.id); })}>Select</button><button className="button button-danger" type="button" disabled={busy || variant.isSelected || activeStates.has(variant.state)} onClick={() => void mutate(async () => { await deleteClipVariant(projectId, sceneId, variant.id); })}>Delete</button></div>
                </article>
              ))}
            </div>
          )}
        </>
      )}
    </section>
  );
}

function VideoHeading() {
  return <div className="section-heading"><div><p className="eyebrow">Block 10 · Animation</p><h2 id="video-heading">Scene video generation</h2><p>Animate approved keyframes through the persistent job engine and keep every successful clip variant recoverable.</p></div></div>;
}

function formatCost(variant: ClipVariantResponse): string {
  const cost = variant.actualCost ?? variant.estimatedCost;
  return cost == null ? "Cost pending" : `${variant.actualCost == null ? "Est. " : ""}${cost.toFixed(2)} ${variant.currency}`;
}
