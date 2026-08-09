import type { ProjectResponse } from "@/src/api/client";

interface ProjectSidebarProps {
  projects: ProjectResponse[];
  selectedProjectId?: string;
  loading: boolean;
  error?: string;
  onCreateNew: () => void;
  onSelect: (project: ProjectResponse) => void;
  onRetry: () => void;
}

export function ProjectSidebar({
  projects,
  selectedProjectId,
  loading,
  error,
  onCreateNew,
  onSelect,
  onRetry,
}: ProjectSidebarProps) {
  return (
    <aside className="studio-sidebar" aria-label="Projects">
      <div className="brand-block">
        <span className="brand-mark">OM</span>
        <div>
          <strong>OpenMusicVideoCreator</strong>
          <span>AI music video studio</span>
        </div>
      </div>

      <button className="button button-primary button-full" type="button" onClick={onCreateNew}>
        + New project
      </button>

      <div className="sidebar-heading">
        <span>Projects</span>
        <span className="count-pill">{projects.length}</span>
      </div>

      {loading ? <p className="muted" aria-live="polite">Loading projects…</p> : null}
      {error ? (
        <div className="inline-state" role="alert">
          <p>{error}</p>
          <button className="button button-ghost" type="button" onClick={onRetry}>Retry</button>
        </div>
      ) : null}
      {!loading && !error && projects.length === 0 ? (
        <p className="muted">No projects yet. Create the first video.</p>
      ) : null}

      <nav className="project-list" aria-label="Saved projects">
        {projects.map((project) => {
          const selected = project.id === selectedProjectId;
          return (
            <button
              key={project.id}
              className={`project-item ${selected ? "is-active" : ""}`}
              type="button"
              onClick={() => onSelect(project)}
              aria-current={selected ? "page" : undefined}
            >
              <strong>{project.title}</strong>
              <span>{project.artist || "Unknown artist"}</span>
              <small>{project.preset.replace("BestQuality", "Best Quality")}</small>
            </button>
          );
        })}
      </nav>
    </aside>
  );
}
