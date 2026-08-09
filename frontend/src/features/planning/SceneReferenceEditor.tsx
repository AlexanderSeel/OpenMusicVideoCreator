import type { StoryboardSceneResponse, VisualLibraryKind, VisualLibraryResponse } from "@/src/api/client";

interface SceneReferenceEditorProps {
  scene: StoryboardSceneResponse;
  library: VisualLibraryResponse[];
  onChange: (patch: Partial<StoryboardSceneResponse>) => void;
}

const groups: Array<{
  kind: VisualLibraryKind;
  label: string;
  key: "characterIds" | "styleIds" | "locationIds";
}> = [
  { kind: "Character", label: "Characters", key: "characterIds" },
  { kind: "Style", label: "Styles", key: "styleIds" },
  { kind: "Location", label: "Locations", key: "locationIds" },
];

export function SceneReferenceEditor({ scene, library, onChange }: SceneReferenceEditorProps) {
  return (
    <div className="scene-reference-editor">
      {groups.map((group) => {
        const options = library.filter((item) => item.kind === group.kind);
        const selected = scene[group.key];
        return (
          <fieldset key={group.kind}>
            <legend>{group.label}</legend>
            {options.length === 0 ? <span className="muted">No reusable {group.kind.toLowerCase()} entries attached to the workspace.</span> : null}
            {options.map((item) => (
              <label key={item.id}>
                <input
                  type="checkbox"
                  checked={selected.includes(item.id)}
                  onChange={(event) => {
                    const next = event.target.checked
                      ? [...selected, item.id]
                      : selected.filter((id) => id !== item.id);
                    onChange({ [group.key]: next });
                  }}
                />
                <span>{item.name}</span>
              </label>
            ))}
          </fieldset>
        );
      })}
    </div>
  );
}
