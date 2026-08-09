"use client";

import { type FormEvent, useEffect, useMemo, useState } from "react";
import {
  createVisualLibraryItem,
  deleteAssetLibrary,
  deleteVisualLibraryItem,
  getAssetPreviewUrl,
  listAssetLibrary,
  listVisualLibrary,
  updateAssetLibrary,
  updateVisualLibraryItem,
  uploadAssetLibrary,
  type AssetLibraryResponse,
  type VisualLibraryKind,
  type VisualLibraryResponse,
  type VisualLibraryUpsertRequest,
} from "@/src/api/client";

interface VisualLibraryPanelProps {
  onChanged?: (items: VisualLibraryResponse[]) => void;
}

type EditorState = {
  id?: string;
  kind: VisualLibraryKind;
  name: string;
  description: string;
  tags: string;
  favorite: boolean;
  assetIds: string[];
  appearance: string;
  forbidden: string;
  outfits: string;
  prompt: string;
  camera: string;
  lighting: string;
  animation: string;
  environment: string;
  constraints: string;
  weather: string;
  timeOfDay: string;
};

function emptyEditor(kind: VisualLibraryKind = "Character"): EditorState {
  return {
    kind, name: "", description: "", tags: "", favorite: false, assetIds: [],
    appearance: "", forbidden: "", outfits: "", prompt: "", camera: "", lighting: "", animation: "",
    environment: "", constraints: "", weather: "", timeOfDay: "",
  };
}

export function VisualLibraryPanel({ onChanged }: VisualLibraryPanelProps) {
  const [items, setItems] = useState<VisualLibraryResponse[]>([]);
  const [assets, setAssets] = useState<AssetLibraryResponse[]>([]);
  const [editor, setEditor] = useState<EditorState>(emptyEditor);
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState<VisualLibraryKind | "All">("All");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);

  async function refresh(signal?: AbortSignal) {
    const [nextItems, nextAssets] = await Promise.all([
      listVisualLibrary(undefined, signal),
      listAssetLibrary(signal),
    ]);
    setItems(nextItems);
    setAssets(nextAssets);
    onChanged?.(nextItems);
  }

  useEffect(() => {
    const controller = new AbortController();
    refresh(controller.signal).catch((error: unknown) => {
      if (!controller.signal.aborted) setMessage(error instanceof Error ? error.message : "Could not load visual library.");
    });
    return () => controller.abort();
    // Parent callback intentionally does not trigger data reloads.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const filtered = useMemo(() => {
    const term = query.trim().toLowerCase();
    return items.filter((item) =>
      (kind === "All" || item.kind === kind) &&
      (!term || item.name.toLowerCase().includes(term) || item.description.toLowerCase().includes(term) || item.tags.some((tag) => tag.toLowerCase().includes(term))));
  }, [items, query, kind]);

  async function saveItem(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setMessage("");
    try {
      const request = buildRequest(editor, items.find((item) => item.id === editor.id));
      const saved = editor.id
        ? await updateVisualLibraryItem(editor.id, request)
        : await createVisualLibraryItem(request);
      await refresh();
      setEditor(emptyEditor(saved.kind));
      setMessage(`${saved.kind} “${saved.name}” saved.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not save library item.");
    } finally {
      setBusy(false);
    }
  }

  async function removeItem(item: VisualLibraryResponse) {
    if (!window.confirm(`Delete ${item.kind.toLowerCase()} “${item.name}”?`)) return;
    setBusy(true);
    try {
      const result = await deleteVisualLibraryItem(item.id);
      if (!result.deleted) {
        setMessage(`Cannot delete “${item.name}”: referenced by ${result.referencingIds.length} project(s).`);
      } else {
        await refresh();
        if (editor.id === item.id) setEditor(emptyEditor(item.kind));
        setMessage(`${item.name} deleted.`);
      }
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not delete library item.");
    } finally {
      setBusy(false);
    }
  }

  async function toggleFavorite(item: VisualLibraryResponse) {
    setBusy(true);
    try {
      await updateVisualLibraryItem(item.id, responseToRequest(item, !item.isFavorite));
      await refresh();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not update favorite.");
    } finally {
      setBusy(false);
    }
  }

  async function uploadAsset(file: File | null) {
    if (!file || busy) return;
    setBusy(true);
    setMessage("");
    try {
      await uploadAssetLibrary(file, file.name.replace(/\.[^.]+$/, ""), [], "Uploaded visual reference");
      await refresh();
      setMessage(`${file.name} added to Asset Library.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not upload visual asset.");
    } finally {
      setBusy(false);
    }
  }

  async function toggleAssetFavorite(asset: AssetLibraryResponse) {
    setBusy(true);
    try {
      await updateAssetLibrary(asset.id, {
        name: asset.name,
        tags: asset.tags,
        isFavorite: !asset.isFavorite,
        sourceDescription: asset.sourceDescription,
      });
      await refresh();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not update asset.");
    } finally {
      setBusy(false);
    }
  }

  async function removeAsset(asset: AssetLibraryResponse) {
    if (!window.confirm(`Remove asset entry “${asset.name}”? Original media is retained for recovery.`)) return;
    setBusy(true);
    try {
      const result = await deleteAssetLibrary(asset.id);
      if (!result.deleted) {
        setMessage(`Cannot remove “${asset.name}”: referenced by ${result.referencingIds.length} library item(s).`);
      } else {
        await refresh();
        setMessage(`${asset.name} removed from the asset index; media bytes were retained.`);
      }
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not remove asset.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="library-panel" aria-labelledby="library-heading">
      <div className="section-heading">
        <div><span>06</span><h2 id="library-heading">Visual Library</h2></div>
        <p>Reusable Characters, Styles, Locations and reference media shared across projects.</p>
      </div>
      {message ? <div className="status-banner" role="status">{message}</div> : null}

      <div className="library-toolbar">
        <label className="field compact-field"><span>Search</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Name, description or tag" /></label>
        <label className="field compact-field"><span>Type</span><select value={kind} onChange={(event) => setKind(event.target.value as VisualLibraryKind | "All")}><option value="All">All</option><option value="Character">Characters</option><option value="Style">Styles</option><option value="Location">Locations</option></select></label>
        <label className="button library-upload">+ Reference asset<input type="file" accept="image/*,video/*" disabled={busy} onChange={(event) => void uploadAsset(event.target.files?.[0] ?? null)} /></label>
      </div>

      <div className="library-layout">
        <div className="library-items">
          <h3>Reusable items</h3>
          {filtered.length === 0 ? <p className="muted">No matching library items.</p> : null}
          <div className="library-card-grid">
            {filtered.map((item) => (
              <article key={item.id} className="library-card">
                <div><span className="type-pill">{item.kind}</span><button className="icon-button" type="button" disabled={busy} onClick={() => void toggleFavorite(item)} aria-label={`${item.isFavorite ? "Remove" : "Add"} favorite`}>{item.isFavorite ? "★" : "☆"}</button></div>
                <strong>{item.name}</strong>
                <p>{item.description}</p>
                <small>{item.tags.join(" · ") || "No tags"}</small>
                <div className="library-card-actions"><button className="button" type="button" onClick={() => setEditor(toEditor(item))}>Edit</button><button className="button button-danger" type="button" disabled={busy} onClick={() => void removeItem(item)}>Delete</button></div>
              </article>
            ))}
          </div>
        </div>

        <form className="library-editor" onSubmit={saveItem}>
          <div className="library-editor-heading"><strong>{editor.id ? "Edit library item" : "New library item"}</strong>{editor.id ? <button type="button" className="button button-ghost" onClick={() => setEditor(emptyEditor(editor.kind))}>New</button> : null}</div>
          <label className="field"><span>Type</span><select value={editor.kind} onChange={(event) => setEditor(emptyEditor(event.target.value as VisualLibraryKind))}><option value="Character">Character</option><option value="Style">Style</option><option value="Location">Location</option></select></label>
          <label className="field"><span>Name *</span><input required value={editor.name} onChange={(event) => setEditor((current) => ({ ...current, name: event.target.value }))} /></label>
          <label className="field"><span>Description</span><textarea rows={3} value={editor.description} onChange={(event) => setEditor((current) => ({ ...current, description: event.target.value }))} /></label>
          <label className="field"><span>Tags</span><input value={editor.tags} onChange={(event) => setEditor((current) => ({ ...current, tags: event.target.value }))} placeholder="hero, night, mystic" /></label>
          <label className="toggle-field"><input type="checkbox" checked={editor.favorite} onChange={(event) => setEditor((current) => ({ ...current, favorite: event.target.checked }))} /><span>Favorite</span></label>
          <AssetPicker assets={assets} selected={editor.assetIds} onChange={(assetIds) => setEditor((current) => ({ ...current, assetIds }))} />
          <KindFields editor={editor} onChange={setEditor} />
          <button className="button button-primary" type="submit" disabled={busy}>{busy ? "Saving…" : editor.id ? "Save changes" : `Create ${editor.kind}`}</button>
        </form>
      </div>

      <div className="asset-library">
        <div className="structure-heading"><div><strong>Asset Library</strong><span>Source-tracked visual references with generated previews.</span></div><span>{assets.length} assets</span></div>
        <div className="asset-grid">
          {assets.map((asset) => (
            <article className="asset-card" key={asset.id}>
              <div className="asset-preview">{asset.previewMediaAssetId ? <img src={getAssetPreviewUrl(asset.id)} alt="" loading="lazy" /> : <span>No preview</span>}</div>
              <div className="asset-meta"><strong>{asset.name}</strong><small>{asset.sourceDescription}</small><span>{asset.tags.join(" · ") || "untagged"}</span></div>
              <div className="asset-actions"><button className="icon-button" type="button" onClick={() => void toggleAssetFavorite(asset)} aria-label={`${asset.isFavorite ? "Remove" : "Add"} asset favorite`}>{asset.isFavorite ? "★" : "☆"}</button><button className="icon-button danger" type="button" onClick={() => void removeAsset(asset)} aria-label="Remove asset entry">×</button></div>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}

function AssetPicker({ assets, selected, onChange }: { assets: AssetLibraryResponse[]; selected: string[]; onChange: (ids: string[]) => void }) {
  return <fieldset className="asset-picker"><legend>Reference assets</legend>{assets.length === 0 ? <p className="muted">Upload reference media first.</p> : assets.map((asset) => <label key={asset.id}><input type="checkbox" checked={selected.includes(asset.id)} onChange={(event) => onChange(event.target.checked ? [...selected, asset.id] : selected.filter((id) => id !== asset.id))} /><span>{asset.name}</span></label>)}</fieldset>;
}

function KindFields({ editor, onChange }: { editor: EditorState; onChange: React.Dispatch<React.SetStateAction<EditorState>> }) {
  if (editor.kind === "Character") {
    return <><label className="field"><span>Appearance</span><textarea rows={3} value={editor.appearance} onChange={(event) => onChange((current) => ({ ...current, appearance: event.target.value }))} /></label><label className="field"><span>Forbidden changes</span><input value={editor.forbidden} onChange={(event) => onChange((current) => ({ ...current, forbidden: event.target.value }))} placeholder="hair color, face shape" /></label><label className="field"><span>Outfits</span><input value={editor.outfits} onChange={(event) => onChange((current) => ({ ...current, outfits: event.target.value }))} placeholder="Streetwear, Formal, Battle worn" /></label></>;
  }
  if (editor.kind === "Style") {
    return <><label className="field"><span>Style prompt</span><textarea rows={3} value={editor.prompt} onChange={(event) => onChange((current) => ({ ...current, prompt: event.target.value }))} /></label><label className="field"><span>Camera</span><input value={editor.camera} onChange={(event) => onChange((current) => ({ ...current, camera: event.target.value }))} /></label><label className="field"><span>Lighting</span><input value={editor.lighting} onChange={(event) => onChange((current) => ({ ...current, lighting: event.target.value }))} /></label><label className="field"><span>Animation</span><input value={editor.animation} onChange={(event) => onChange((current) => ({ ...current, animation: event.target.value }))} /></label></>;
  }
  return <><label className="field"><span>Environment</span><textarea rows={3} value={editor.environment} onChange={(event) => onChange((current) => ({ ...current, environment: event.target.value }))} /></label><label className="field"><span>Constraints</span><input value={editor.constraints} onChange={(event) => onChange((current) => ({ ...current, constraints: event.target.value }))} /></label><label className="field"><span>Lighting</span><input value={editor.lighting} onChange={(event) => onChange((current) => ({ ...current, lighting: event.target.value }))} /></label><label className="field"><span>Weather</span><input value={editor.weather} onChange={(event) => onChange((current) => ({ ...current, weather: event.target.value }))} /></label><label className="field"><span>Time of day</span><input value={editor.timeOfDay} onChange={(event) => onChange((current) => ({ ...current, timeOfDay: event.target.value }))} /></label></>;
}

function buildRequest(editor: EditorState, existing?: VisualLibraryResponse): VisualLibraryUpsertRequest {
  const tags = split(editor.tags);
  const shared = { kind: editor.kind, name: editor.name, description: editor.description, tags, isFavorite: editor.favorite, assetEntryIds: editor.assetIds, character: null, style: null, location: null } satisfies VisualLibraryUpsertRequest;
  if (editor.kind === "Character") {
    const existingOutfits = existing?.character?.outfits ?? [];
    const outfits = split(editor.outfits).map((name) => existingOutfits.find((outfit) => outfit.name === name) ?? { id: crypto.randomUUID(), name, description: "", assetEntryIds: [] });
    return { ...shared, character: { referenceType: existing?.character?.referenceType ?? "Photo", appearanceDescription: editor.appearance, forbiddenChanges: split(editor.forbidden), outfits, defaultLocks: existing?.character?.defaultLocks ?? { identity: true, face: true, hair: true, body: true, age: true, wardrobe: true } } };
  }
  if (editor.kind === "Style") return { ...shared, style: { prompt: editor.prompt, cameraCharacteristics: editor.camera, lightingCharacteristics: editor.lighting, animationCharacteristics: editor.animation } };
  return { ...shared, location: { environmentDescription: editor.environment, constraints: split(editor.constraints), lighting: editor.lighting, weather: editor.weather, timeOfDay: editor.timeOfDay } };
}

function responseToRequest(item: VisualLibraryResponse, favorite = item.isFavorite): VisualLibraryUpsertRequest {
  return { kind: item.kind, name: item.name, description: item.description, tags: item.tags, isFavorite: favorite, assetEntryIds: item.assetEntryIds, character: item.character ?? null, style: item.style ?? null, location: item.location ?? null };
}

function toEditor(item: VisualLibraryResponse): EditorState {
  return { id: item.id, kind: item.kind, name: item.name, description: item.description, tags: item.tags.join(", "), favorite: item.isFavorite, assetIds: item.assetEntryIds, appearance: item.character?.appearanceDescription ?? "", forbidden: item.character?.forbiddenChanges.join(", ") ?? "", outfits: item.character?.outfits.map((outfit) => outfit.name).join(", ") ?? "", prompt: item.style?.prompt ?? "", camera: item.style?.cameraCharacteristics ?? "", lighting: item.style?.lightingCharacteristics ?? item.location?.lighting ?? "", animation: item.style?.animationCharacteristics ?? "", environment: item.location?.environmentDescription ?? "", constraints: item.location?.constraints.join(", ") ?? "", weather: item.location?.weather ?? "", timeOfDay: item.location?.timeOfDay ?? "" };
}

function split(value: string): string[] { return value.split(",").map((part) => part.trim()).filter(Boolean); }
