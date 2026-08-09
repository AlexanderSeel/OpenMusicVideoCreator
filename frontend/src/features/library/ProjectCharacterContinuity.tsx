"use client";

import { useEffect, useMemo, useState } from "react";
import {
  listProjectCharacterStates,
  saveProjectCharacterState,
  type ProjectCharacterStateRequest,
  type ProjectCharacterStateResponse,
  type VisualLibraryResponse,
} from "@/src/api/client";

interface ProjectCharacterContinuityProps {
  projectId?: string;
  selectedCharacterIds: string[];
  characters: VisualLibraryResponse[];
}

const stateKeys = ["presence", "confidence", "isolation"] as const;

export function ProjectCharacterContinuity({
  projectId,
  selectedCharacterIds,
  characters,
}: ProjectCharacterContinuityProps) {
  const [states, setStates] = useState<ProjectCharacterStateResponse[]>([]);
  const [message, setMessage] = useState("");
  const [savingId, setSavingId] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    if (!projectId) {
      setStates([]);
      return () => controller.abort();
    }
    listProjectCharacterStates(projectId, controller.signal)
      .then(setStates)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setMessage(error instanceof Error ? error.message : "Could not load character continuity.");
        }
      });
    return () => controller.abort();
  }, [projectId]);

  const selectedCharacters = useMemo(
    () => characters.filter((character) => selectedCharacterIds.includes(character.id)),
    [characters, selectedCharacterIds],
  );

  if (selectedCharacters.length === 0) {
    return null;
  }

  return (
    <div className="continuity-panel">
      <div className="continuity-heading">
        <div>
          <strong>Character continuity</strong>
          <span>Project-specific locks and starting state. Global Character metadata stays reusable.</span>
        </div>
        {!projectId ? <em>Save the project before continuity settings.</em> : null}
      </div>
      {message ? <div className="status-banner" role="status">{message}</div> : null}
      <div className="continuity-grid">
        {selectedCharacters.map((character) => {
          const existing = states.find((state) => state.characterId === character.id);
          return (
            <CharacterStateCard
              key={`${character.id}-${existing?.updatedUtc ?? "new"}`}
              character={character}
              existing={existing}
              disabled={!projectId || savingId === character.id}
              onSave={async (request) => {
                if (!projectId) return;
                setSavingId(character.id);
                setMessage("");
                try {
                  const saved = await saveProjectCharacterState(projectId, character.id, request);
                  setStates((current) => [...current.filter((state) => state.characterId !== character.id), saved]);
                  setMessage(`${character.name} continuity saved.`);
                } catch (error) {
                  setMessage(error instanceof Error ? error.message : "Could not save character continuity.");
                } finally {
                  setSavingId(null);
                }
              }}
            />
          );
        })}
      </div>
    </div>
  );
}

interface CharacterStateCardProps {
  character: VisualLibraryResponse;
  existing?: ProjectCharacterStateResponse;
  disabled: boolean;
  onSave: (request: ProjectCharacterStateRequest) => Promise<void>;
}

function CharacterStateCard({ character, existing, disabled, onSave }: CharacterStateCardProps) {
  const defaults = character.character?.defaultLocks ?? {
    identity: true,
    face: true,
    hair: true,
    body: true,
    age: true,
    wardrobe: true,
  };
  const [outfitId, setOutfitId] = useState(existing?.outfitId ?? "");
  const [locks, setLocks] = useState(existing?.locks ?? defaults);
  const [values, setValues] = useState<Record<string, number>>({
    presence: existing?.stateValues.presence ?? 1,
    confidence: existing?.stateValues.confidence ?? 0.5,
    isolation: existing?.stateValues.isolation ?? 0,
    ...existing?.stateValues,
  });

  return (
    <article className="continuity-card">
      <div className="continuity-card-title">
        <strong>{character.name}</strong>
        <span>{character.character?.appearanceDescription || "Character reference"}</span>
      </div>
      <label className="field compact-field">
        <span>Outfit</span>
        <select value={outfitId} disabled={disabled} onChange={(event) => setOutfitId(event.target.value)}>
          <option value="">Default / unspecified</option>
          {(character.character?.outfits ?? []).map((outfit) => (
            <option key={outfit.id} value={outfit.id}>{outfit.name}</option>
          ))}
        </select>
      </label>
      <div className="lock-grid" aria-label={`${character.name} continuity locks`}>
        {(Object.keys(locks) as Array<keyof typeof locks>).map((key) => (
          <label key={key}>
            <input
              type="checkbox"
              checked={locks[key]}
              disabled={disabled}
              onChange={(event) => setLocks((current) => ({ ...current, [key]: event.target.checked }))}
            />
            <span>{key}</span>
          </label>
        ))}
      </div>
      <div className="state-sliders">
        {stateKeys.map((key) => (
          <label key={key}>
            <span>{key} <output>{Math.round((values[key] ?? 0) * 100)}%</output></span>
            <input
              type="range"
              min="0"
              max="1"
              step="0.05"
              disabled={disabled}
              value={values[key] ?? 0}
              onChange={(event) => setValues((current) => ({ ...current, [key]: Number(event.target.value) }))}
            />
          </label>
        ))}
      </div>
      <button
        className="button"
        type="button"
        disabled={disabled}
        onClick={() => void onSave({ outfitId: outfitId || null, locks, stateValues: values })}
      >
        Save continuity
      </button>
    </article>
  );
}
