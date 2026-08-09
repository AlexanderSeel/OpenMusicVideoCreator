import type {
  ProjectUpsertRequest,
  VisualLibraryKind,
  VisualLibraryResponse,
} from "@/src/api/client";

interface VisualReferenceSelectorProps {
  kind: VisualLibraryKind;
  description: string;
  items: VisualLibraryResponse[];
  references: NonNullable<ProjectUpsertRequest["references"]>;
  onChange: (references: NonNullable<ProjectUpsertRequest["references"]>) => void;
}

export function VisualReferenceSelector({
  kind,
  description,
  items,
  references,
  onChange,
}: VisualReferenceSelectorProps) {
  const selected = new Set(
    references.filter((reference) => reference.kind === kind).map((reference) => reference.referenceId),
  );

  function toggle(id: string) {
    const otherReferences = references.filter((reference) => reference.kind !== kind);
    const selectedIds = selected.has(id)
      ? [...selected].filter((selectedId) => selectedId !== id)
      : [...selected, id];
    onChange([
      ...otherReferences,
      ...selectedIds.map((referenceId) => ({ kind, referenceId })),
    ]);
  }

  return (
    <fieldset className="reference-selector">
      <legend>{kind}</legend>
      <p>{description}</p>
      {items.length === 0 ? (
        <div className="reference-empty">No {kind.toLowerCase()} items yet. Add one in the Library below.</div>
      ) : (
        <div className="reference-options">
          {items.map((item) => (
            <label key={item.id} className={`reference-option ${selected.has(item.id) ? "is-selected" : ""}`}>
              <input
                type="checkbox"
                checked={selected.has(item.id)}
                onChange={() => toggle(item.id)}
              />
              <span>
                <strong>{item.name}</strong>
                <small>{item.description || item.tags.join(" · ") || "Reusable visual reference"}</small>
              </span>
              {item.isFavorite ? <em aria-label="Favorite">★</em> : null}
            </label>
          ))}
        </div>
      )}
    </fieldset>
  );
}
