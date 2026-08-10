"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { getProviderCatalog, getStoryboard, type StoryboardResponse } from "@/src/api/client";
import {
  approveKeyframes,
  deleteKeyframeVariant,
  generateKeyframes,
  getKeyframeApproval,
  getKeyframePreviewUrl,
  getKeyframeSettings,
  listKeyframeVariants,
  revokeKeyframeApproval,
  saveKeyframeSettings,
  selectKeyframeVariant,
  type KeyframeApprovalStatusResponse,
  type KeyframeGenerationSettingsRequest,
  type KeyframeGenerationSettingsResponse,
  type KeyframeRole,
  type KeyframeVariantResponse,
} from "@/src/api/keyframes";

export type StudioMode = "Simple" | "Advanced" | "Custom";

interface KeyframeWorkspaceProps {
  projectId?: string;
  mode: StudioMode;
}

type ProviderCatalog = Awaited<ReturnType<typeof getProviderCatalog>>;
type Provider = ProviderCatalog[number];
type ProviderModel = Provider["models"][number];

const activeStates = new Set(["Planned", "Queued", "Generating"]);

export function KeyframeWorkspace({ projectId, mode }: KeyframeWorkspaceProps) {
  const [storyboard, setStoryboard] = useState<StoryboardResponse | null>(null);
  const [sceneId, setSceneId] = useState("");
  const [variants, setVariants] = useState<KeyframeVariantResponse[]>([]);
  const [settings, setSettings] = useState<KeyframeGenerationSettingsResponse | null>(null);
  const [approval, setApproval] = useState<KeyframeApprovalStatusResponse | null>(null);
  const [providers, setProviders] = useState<ProviderCatalog>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const showAdvancedSettings = mode !== "Simple";

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
      listKeyframeVariants(projectId, currentSceneId, signal),
      getKeyframeSettings(projectId, currentSceneId, signal),
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
        if (!controller.signal.aborted) setMessage(error instanceof Error ? error.message : "Could not load keyframe workspace.");
      });
    return () => controller.abort();
  }, [loadStoryboard]);

  useEffect(() => {
    const controller = new AbortController();
    loadSceneState(sceneId, controller.signal)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setMessage(error instanceof Error ? error.message : "Could not load keyframe scene state.");
      });
    return () => controller.abort();
  }, [loadSceneState, sceneId]);

  useEffect(() => {
    if (!projectId) return;
    const timer = window.setInterval(() => {
      void loadStoryboard().catch(() => undefined);
    }, storyboard ? 5000 : 2000);
    return () => window.clearInterval(timer);
  }, [loadStoryboard, projectId, storyboard]);

  const hasActiveVariants = variants.some((variant) => activeStates.has(variant.state));
  useEffect(() => {
    if (!hasActiveVariants || !sceneId) return;
    const timer = window.setInterval(() => {
      void loadSceneState(sceneId).catch(() => undefined);
    }, 1500);
    return () => window.clearInterval(timer);
  }, [hasActiveVariants, loadSceneState, sceneId]);

  const imageProviders = useMemo(
    () => providers.filter((provider) => provider.settings.enabled && provider.models.some((model) => model.capabilities.includes("ImageGeneration"))),
    [providers],
  );
  const selectedProvider = useMemo(
    () => imageProviders.find((provider) => provider.id === settings?.providerId) ?? null,
    [imageProviders, settings?.providerId],
  );
  const selectedModel = useMemo(
    () => selectedProvider?.models.find((model) => model.modelId === settings?.modelId) ?? null,
    [selectedProvider, settings?.modelId],
  );
  const scene = storyboard?.scenes.find((candidate) => candidate.id === sceneId) ?? null;
  const startVariants = variants.filter((variant) => variant.role === "Start");
  const endVariants = variants.filter((variant) => variant.role === "End");

  async function mutate(action: () => Promise<void>) {
    if (busy) return;
    setBusy(true);
    setMessage("");
    try {
      await action();
      if (sceneId) await loadSceneState(sceneId);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Keyframe action failed.");
    } finally {
      setBusy(false);
    }
  }

  async function queue(role?: KeyframeRole) {
    if (!projectId || !sceneId) return;
    await mutate(async () => {
      const result = await generateKeyframes(projectId, sceneId, role);
      setVariants((current) => [...current, ...result.variants]);
      setMessage(role ? `${role} keyframe regeneration queued.` : "Scene keyframe generation queued.");
    });
  }

  async function saveSettings() {
    if (!projectId || !sceneId || !settings || !showAdvancedSettings) return;
    await mutate(async () => {
      const request: KeyframeGenerationSettingsRequest = {
        providerId: settings.providerId,
        modelId: settings.modelId,
        generateEndFrame: settings.generateEndFrame,
        resolution: settings.resolution,
        seed: settings.seed,
        negativePrompt: settings.negativePrompt,
      };
      setSettings(await saveKeyframeSettings(projectId, sceneId, request));
      setMessage("Scene keyframe settings saved.");
    });
  }

  function patchSettings(patch: Partial<KeyframeGenerationSettingsResponse>) {
    setSettings((current) => current ? { ...current, ...patch } : current);
  }

  function chooseProvider(providerId: string) {
    if (!settings || !showAdvancedSettings) return;
    if (!providerId) {
      patchSettings({ providerId: null, modelId: null, resolution: null, seed: null, negativePrompt: null });
      return;
    }
    const provider = imageProviders.find((candidate) => candidate.id === providerId);
    const model = provider?.models.find((candidate) => candidate.capabilities.includes("ImageGeneration"));
    patchSettings({
      providerId,
      modelId: model?.modelId ?? null,
      resolution: model?.supportedResolutions[0] ?? null,
      seed: model?.supportsSeed ? settings.seed : null,
      negativePrompt: model?.supportsNegativePrompt ? settings.negativePrompt : null,
    });
  }

  function chooseModel(modelId: string) {
    if (!showAdvancedSettings) return;
    const model = selectedProvider?.models.find((candidate) => candidate.modelId === modelId) ?? null;
    patchSettings({
      modelId: model?.modelId ?? null,
      resolution: model?.supportedResolutions[0] ?? null,
      seed: model?.supportsSeed ? settings?.seed ?? null : null,
      negativePrompt: model?.supportsNegativePrompt ? settings?.negativePrompt ?? null : null,
    });
  }

  if (!projectId) {
    return <section className="keyframe-workspace keyframe-empty"><KeyframeHeading /><p>Save the project and create a storyboard before generating keyframes.</p></section>;
  }

  return (
    <section className="keyframe-workspace" aria-labelledby="keyframe-heading">
      <KeyframeHeading />
      {message ? <div className="status-banner" role="status">{message}</div> : null}

      {!storyboard ? (
        <div className="keyframe-callout"><strong>No storyboard available</strong><span>Create a Director plan first. This section refreshes automatically when a storyboard becomes available.</span><button className="button" type="button" onClick={() => void loadStoryboard()}>Refresh storyboard</button></div>
      ) : (
        <>
          <div className="keyframe-toolbar">
            <label className="field"><span>Scene</span><select value={sceneId} onChange={(event) => setSceneId(event.target.value)}>{storyboard.scenes.map((item) => <option key={item.id} value={item.id}>Scene {item.sequence} · {item.title}</option>)}</select></label>
            <div className="keyframe-toolbar-copy"><strong>{scene?.details.songSection ?? "Scene"}</strong><span>{scene?.directorIntent}</span></div>
            <button className="button button-primary" type="button" disabled={busy || !sceneId} onClick={() => void queue()}>{busy ? "Working…" : settings?.generateEndFrame ? "Generate start + end" : "Generate start keyframe"}</button>
          </div>

          {settings && showAdvancedSettings ? (
            <details className="keyframe-settings">
              <summary>Advanced / Custom generation settings</summary>
              <div className="keyframe-settings-grid">
                <label className="field"><span>Provider routing</span><select value={settings.providerId ?? ""} onChange={(event) => chooseProvider(event.target.value)}><option value="">Automatic capability routing</option>{imageProviders.map((provider) => <option key={provider.id} value={provider.id}>{provider.displayName}</option>)}</select></label>
                <label className="field"><span>Model</span><select value={settings.modelId ?? ""} disabled={!selectedProvider} onChange={(event) => chooseModel(event.target.value)}><option value="">Automatic</option>{selectedProvider?.models.filter((model) => model.capabilities.includes("ImageGeneration")).map((model) => <option key={model.modelId} value={model.modelId}>{model.displayName}</option>)}</select></label>
                <label className="field"><span>Resolution</span><select value={settings.resolution ?? ""} disabled={!selectedModel || selectedModel.supportedResolutions.length === 0} onChange={(event) => patchSettings({ resolution: event.target.value || null })}><option value="">Project / automatic</option>{selectedModel?.supportedResolutions.map((resolution) => <option key={resolution} value={resolution}>{resolution}</option>)}</select></label>
                <label className="toggle-row"><input type="checkbox" checked={settings.generateEndFrame} onChange={(event) => patchSettings({ generateEndFrame: event.target.checked })} /><span>Generate optional end keyframe</span></label>
                {selectedModel?.supportsSeed ? <label className="field"><span>Seed</span><input type="number" value={settings.seed ?? ""} onChange={(event) => patchSettings({ seed: event.target.value ? Number(event.target.value) : null })} /></label> : null}
                {selectedModel?.supportsNegativePrompt ? <label className="field keyframe-wide"><span>Negative prompt</span><textarea rows={2} value={settings.negativePrompt ?? ""} onChange={(event) => patchSettings({ negativePrompt: event.target.value || null })} /></label> : null}
              </div>
              <div className="keyframe-settings-note">Reference support: {selectedModel ? selectedModel.supportsReferences ? `up to ${selectedModel.maxReferences} attached library references` : "not supported by this model" : "resolved automatically by provider capability"}.</div>
              <button className="button" type="button" disabled={busy} onClick={() => void saveSettings()}>Save scene settings</button>
            </details>
          ) : mode === "Simple" ? (
            <div className="keyframe-simple-note">Simple Mode uses automatic provider/model routing and hides seed, negative-prompt, and provider-specific controls. Switch to Advanced or Custom to edit per-scene generation settings.</div>
          ) : null}

          <div className="keyframe-role-section">
            <div className="structure-heading"><div><strong>Start keyframes</strong><span>Regeneration appends a new variant; the selected successful variant remains intact.</span></div><button className="button" type="button" disabled={busy} onClick={() => void queue("Start")}>Regenerate start</button></div>
            <VariantGrid variants={startVariants} projectId={projectId} sceneId={sceneId} busy={busy} showProviderDetails={showAdvancedSettings} onSelect={(id) => mutate(async () => { await selectKeyframeVariant(projectId, sceneId, id); setApproval(await getKeyframeApproval(projectId, sceneId)); })} onDelete={(id) => mutate(async () => { await deleteKeyframeVariant(projectId, sceneId, id); })} />
          </div>

          <div className="keyframe-role-section">
            <div className="structure-heading"><div><strong>End keyframes</strong><span>Optional end frames can guide later image-to-video generation when supported.</span></div><button className="button" type="button" disabled={busy} onClick={() => void queue("End")}>Generate / regenerate end</button></div>
            <VariantGrid variants={endVariants} projectId={projectId} sceneId={sceneId} busy={busy} showProviderDetails={showAdvancedSettings} onSelect={(id) => mutate(async () => { await selectKeyframeVariant(projectId, sceneId, id); setApproval(await getKeyframeApproval(projectId, sceneId)); })} onDelete={(id) => mutate(async () => { await deleteKeyframeVariant(projectId, sceneId, id); })} />
          </div>

          <div className={`keyframe-approval ${approval?.isApproved ? "is-approved" : ""}`}>
            <div><strong>{approval?.isApproved ? "Keyframes approved" : "Approval required before animation"}</strong><span>{approval?.isApproved ? "The current selected start/end variants are locked as the approved animation input." : "Select a completed Start variant, optionally an End variant, then approve this scene before video generation."}</span></div>
            {approval?.isApproved ? <button className="button" type="button" disabled={busy} onClick={() => void mutate(async () => { await revokeKeyframeApproval(projectId, sceneId); setApproval(await getKeyframeApproval(projectId, sceneId)); })}>Revoke approval</button> : <button className="button button-primary" type="button" disabled={busy || !startVariants.some((variant) => variant.isSelected && variant.state === "Completed")} onClick={() => void mutate(async () => { setApproval(await approveKeyframes(projectId, sceneId)); })}>Approve for animation</button>}
          </div>
        </>
      )}
    </section>
  );
}

function VariantGrid({ variants, projectId, sceneId, busy, showProviderDetails, onSelect, onDelete }: { variants: KeyframeVariantResponse[]; projectId: string; sceneId: string; busy: boolean; showProviderDetails: boolean; onSelect: (id: string) => void; onDelete: (id: string) => void }) {
  if (variants.length === 0) return <div className="keyframe-empty-grid">No variants yet.</div>;
  return (
    <div className="keyframe-variant-grid">
      {variants.map((variant) => (
        <article className={`keyframe-variant ${variant.isSelected ? "is-selected" : ""}`} key={variant.id}>
          <div className="keyframe-preview" role="img" aria-label={`${variant.role} keyframe variant ${variant.variantNumber}`} style={variant.state === "Completed" ? { backgroundImage: `url("${getKeyframePreviewUrl(projectId, sceneId, variant.id)}")` } : undefined}><span>{variant.state}</span></div>
          <div className="keyframe-variant-meta"><div><strong>Variant {variant.variantNumber}</strong>{variant.isSelected ? <em>Selected</em> : null}</div>{showProviderDetails ? <span>{variant.providerId ?? "auto"} · {variant.modelId ?? "model pending"}</span> : <span>Automatic generation · provider details hidden in Simple Mode</span>}<span>{showProviderDetails ? `Prompt ${variant.promptVersionId.slice(0, 8)} · ` : "Prompt provenance stored · "}{formatCost(variant)}</span></div>
          <div className="keyframe-variant-actions"><button className="button" type="button" disabled={busy || variant.state !== "Completed" || variant.isSelected} onClick={() => onSelect(variant.id)}>Select</button><button className="button button-danger" type="button" disabled={busy || variant.isSelected} onClick={() => onDelete(variant.id)}>Delete</button></div>
        </article>
      ))}
    </div>
  );
}

function KeyframeHeading() {
  return <div className="section-heading"><div><span>08</span><h2 id="keyframe-heading">Keyframes & variants</h2></div><p>Generate asynchronously, compare non-destructive variants, select the scene input, then approve it for animation.</p></div>;
}

function formatCost(variant: KeyframeVariantResponse): string {
  const cost = variant.actualCost ?? variant.estimatedCost;
  return cost == null ? "cost pending" : `${cost.toFixed(4)} ${variant.currency}`;
}
