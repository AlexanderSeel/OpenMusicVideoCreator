"use client";

import { TimelineAnalysisLanes } from "./TimelineAnalysisLanes";

export function AdvancedTimelineAnalysisPanel({ projectId }: { projectId?: string }) {
  if (!projectId) return null;
  return (
    <section className="advanced-timeline analysis-timeline-panel" aria-labelledby="analysis-timeline-heading">
      <div className="section-heading">
        <div><span>10</span><h2 id="analysis-timeline-heading">Music reference lanes</h2></div>
        <p>Same persisted Song Analysis used by storyboard planning: waveform, structure, beat/bar and lyric timing.</p>
      </div>
      <div className="timeline-scroll" aria-label="Music analysis timeline lanes">
        <TimelineAnalysisLanes projectId={projectId} durationSeconds={0} />
      </div>
    </section>
  );
}
