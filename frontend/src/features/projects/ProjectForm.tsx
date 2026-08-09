import type { FormEvent } from "react";
import type { ProjectSongResponse } from "@/src/api/client";
import { formatBytes, type EditorState } from "./projectModel";

interface ProjectFormProps {
  editor: EditorState;
  selectedSong: File | null;
  song: ProjectSongResponse | null;
  online: boolean;
  saving: boolean;
  message: string;
  onFieldChange: <K extends keyof EditorState>(key: K, value: EditorState[K]) => void;
  onSongSelected: (file: File | null) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onDelete: () => void;
}

const referencePlaceholders = [
  ["Character", "Keep faces, outfits and identity consistent"],
  ["Style", "Define visual language, lighting and camera feel"],
  ["Location", "Reuse environments and continuity constraints"],
] as const;

export function ProjectForm({
  editor,
  selectedSong,
  song,
  online,
  saving,
  message,
  onFieldChange,
  onSongSelected,
  onSubmit,
  onDelete,
}: ProjectFormProps) {
  return (
    <form className="editor-form" onSubmit={onSubmit}>
      {!online ? (
        <div className="status-banner warning" role="status">
          You are offline. Existing data stays visible; saving resumes when the API is reachable.
        </div>
      ) : null}
      {message ? <div className="status-banner" role="status">{message}</div> : null}

      <section className="form-section" aria-labelledby="identity-heading">
        <SectionHeading
          index="01"
          id="identity-heading"
          title="Song & identity"
          description="Start with the music and the words the video must serve."
        />
        <div className="field-grid two-columns">
          <label className="field">
            <span>Project title *</span>
            <input
              required
              value={editor.title}
              onChange={(event) => onFieldChange("title", event.target.value)}
              placeholder="In the Next Life"
            />
          </label>
          <label className="field">
            <span>Artist</span>
            <input
              value={editor.artist}
              onChange={(event) => onFieldChange("artist", event.target.value)}
              placeholder="Artist name"
            />
          </label>
        </div>
        <label className="upload-card">
          <input
            type="file"
            accept="audio/*,.mp3,.wav,.m4a,.aac,.flac,.ogg,.opus,.webm"
            onChange={(event) => onSongSelected(event.target.files?.[0] ?? null)}
          />
          <div>
            <strong>{selectedSong ? selectedSong.name : song ? "Song attached" : "Choose song"}</strong>
            <span>
              {selectedSong
                ? formatBytes(selectedSong.size)
                : song
                  ? `${song.mimeType} • ${formatBytes(song.fileSize)}`
                  : "MP3, WAV, M4A, AAC, FLAC, OGG, OPUS or WebM"}
            </span>
          </div>
          <span className="upload-action">Browse</span>
        </label>
        <label className="field">
          <span>Lyrics</span>
          <textarea
            rows={8}
            value={editor.lyrics}
            onChange={(event) => onFieldChange("lyrics", event.target.value)}
            placeholder="Paste the authoritative lyrics here…"
          />
        </label>
      </section>

      <section className="form-section" aria-labelledby="story-heading">
        <SectionHeading
          index="02"
          id="story-heading"
          title="Meaning & visual direction"
          description="Tell the Director what the song means before describing shots."
        />
        <div className="field-grid two-columns">
          <label className="field">
            <span>Storyline</span>
            <textarea
              rows={5}
              value={editor.storyline}
              onChange={(event) => onFieldChange("storyline", event.target.value)}
              placeholder="What happens across the video?"
            />
          </label>
          <label className="field">
            <span>Meaning</span>
            <textarea
              rows={5}
              value={editor.meaning}
              onChange={(event) => onFieldChange("meaning", event.target.value)}
              placeholder="What should the viewer feel or understand?"
            />
          </label>
        </div>
        <label className="field">
          <span>Visual direction</span>
          <textarea
            rows={4}
            value={editor.visualDirection}
            onChange={(event) => onFieldChange("visualDirection", event.target.value)}
            placeholder="Mystic, intimate, cinematic, restrained camera…"
          />
        </label>
        <div className="field-grid two-columns">
          <label className="field">
            <span>Mood</span>
            <input
              value={editor.mood}
              onChange={(event) => onFieldChange("mood", event.target.value)}
              placeholder="Hopeful, melancholic, surreal"
            />
          </label>
          <label className="field">
            <span>Genre</span>
            <input
              value={editor.genre}
              onChange={(event) => onFieldChange("genre", event.target.value)}
              placeholder="D&B, rap, trance"
            />
          </label>
        </div>
      </section>

      <section className="form-section" aria-labelledby="references-heading">
        <SectionHeading
          index="03"
          id="references-heading"
          title="Visual references"
          description="Reusable libraries arrive in Block 7; Simple Mode already reserves their place."
        />
        <div className="reference-grid">
          {referencePlaceholders.map(([title, description]) => (
            <button key={title} className="reference-card" type="button" disabled>
              <span className="reference-icon">+</span>
              <strong>{title}</strong>
              <small>{description}</small>
              <em>Library coming in Block 7</em>
            </button>
          ))}
        </div>
      </section>

      <section className="form-section" aria-labelledby="output-heading">
        <SectionHeading
          index="04"
          id="output-heading"
          title="Output & generation strategy"
          description="Choose intent-level settings; provider details stay hidden in Simple Mode."
        />
        <div className="field-grid three-columns">
          <label className="field">
            <span>Aspect ratio</span>
            <select
              value={editor.aspectRatio}
              onChange={(event) => onFieldChange("aspectRatio", event.target.value as EditorState["aspectRatio"])}
            >
              <option value="Landscape16x9">16:9 Landscape</option>
              <option value="Portrait9x16">9:16 Portrait</option>
              <option value="Square1x1">1:1 Square</option>
            </select>
          </label>
          <label className="field">
            <span>Preset</span>
            <select
              value={editor.preset}
              onChange={(event) => onFieldChange("preset", event.target.value as EditorState["preset"])}
            >
              <option value="Fast">Fast</option>
              <option value="Balanced">Balanced</option>
              <option value="BestQuality">Best Quality</option>
              <option value="Cheapest">Cheapest</option>
              <option value="Custom">Custom</option>
            </select>
          </label>
          <label className="field">
            <span>Target platform</span>
            <select
              value={editor.targetPlatforms?.[0] ?? "YouTube"}
              onChange={(event) => onFieldChange("targetPlatforms", [event.target.value])}
            >
              <option>YouTube</option>
              <option>TikTok</option>
              <option>Instagram</option>
              <option>Vimeo</option>
              <option>Local file</option>
            </select>
          </label>
        </div>
        <div className="field-grid two-columns">
          <BudgetField
            label="Estimated budget"
            value={editor.estimatedBudget}
            onChange={(value) => onFieldChange("estimatedBudget", value)}
          />
          <BudgetField
            label="Maximum budget"
            value={editor.maximumBudget}
            onChange={(value) => onFieldChange("maximumBudget", value)}
          />
        </div>
      </section>

      <footer className="editor-actions">
        <div>
          <strong>{editor.id ? "Editing saved project" : "New project"}</strong>
          <span>{editor.id ? "Changes persist in DuckDB when saved." : "Create the project before analysis begins."}</span>
        </div>
        <div className="action-buttons">
          {editor.id ? (
            <button className="button button-danger" type="button" disabled={saving} onClick={onDelete}>
              Delete
            </button>
          ) : null}
          <button className="button button-primary" type="submit" disabled={saving || !online}>
            {saving ? "Saving…" : editor.id ? "Save project" : "Create project"}
          </button>
        </div>
      </footer>
    </form>
  );
}

interface SectionHeadingProps {
  index: string;
  id: string;
  title: string;
  description: string;
}

function SectionHeading({ index, id, title, description }: SectionHeadingProps) {
  return (
    <div className="section-heading">
      <div>
        <span>{index}</span>
        <h2 id={id}>{title}</h2>
      </div>
      <p>{description}</p>
    </div>
  );
}

interface BudgetFieldProps {
  label: string;
  value?: number | null;
  onChange: (value: number | null) => void;
}

function BudgetField({ label, value, onChange }: BudgetFieldProps) {
  return (
    <label className="field">
      <span>{label}</span>
      <div className="money-input">
        <span>€</span>
        <input
          type="number"
          min="0"
          step="0.01"
          value={value ?? ""}
          onChange={(event) => onChange(event.target.value === "" ? null : Number(event.target.value))}
        />
      </div>
    </label>
  );
}
