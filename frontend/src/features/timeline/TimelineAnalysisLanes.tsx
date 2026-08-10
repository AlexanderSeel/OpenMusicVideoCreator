"use client";

import { useEffect, useMemo, useState } from "react";
import {
  getLyricTiming,
  getSongAnalysis,
  type LyricTimingResponse,
  type SongAnalysisResponse,
} from "@/src/api/client";

interface TimelineAnalysisLanesProps {
  projectId: string;
  durationSeconds: number;
}

export function TimelineAnalysisLanes({ projectId, durationSeconds }: TimelineAnalysisLanesProps) {
  const [analysis, setAnalysis] = useState<SongAnalysisResponse | null>(null);
  const [lyrics, setLyrics] = useState<LyricTimingResponse | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([
      getSongAnalysis(projectId, controller.signal),
      getLyricTiming(projectId, controller.signal),
    ]).then(([nextAnalysis, nextLyrics]) => {
      setAnalysis(nextAnalysis);
      setLyrics(nextLyrics);
    }).catch(() => {
      if (!controller.signal.aborted) {
        setAnalysis(null);
        setLyrics(null);
      }
    });
    return () => controller.abort();
  }, [projectId]);

  const duration = Math.max(0.001, analysis?.durationSeconds ?? durationSeconds);
  const timedLyrics = useMemo(
    () => analysis && lyrics?.songAnalysisId === analysis.id
      ? lyrics.lines.filter((line) => line.isMatched && line.startSeconds !== null && line.endSeconds !== null)
      : [],
    [analysis, lyrics],
  );

  if (!analysis) {
    return (
      <div className="timeline-lane analysis-lane">
        <span className="lane-label">Analysis</span>
        <div className="lane-content"><span className="lane-empty">Run Song Analysis to show waveform, structure, beats, bars and lyric timing.</span></div>
      </div>
    );
  }

  return (
    <>
      <div className="timeline-lane waveform-lane">
        <span className="lane-label">Waveform</span>
        <div className="lane-content timeline-analysis-canvas">
          <svg viewBox="0 0 1000 80" preserveAspectRatio="none" role="img" aria-label="Advanced editor song waveform">
            {analysis.quietRanges.map((range, index) => <rect key={`q-${index}`} x={pct(range.startSeconds, duration) * 10} y="0" width={Math.max(1, pct(range.endSeconds - range.startSeconds, duration) * 10)} height="80" className="timeline-quiet" />)}
            <line x1="0" x2="1000" y1="40" y2="40" className="timeline-wave-zero" />
            {analysis.waveform.map((bucket, index) => {
              const x = ((bucket.startSeconds + bucket.endSeconds) / 2 / duration) * 1000;
              return <line key={index} x1={x} x2={x} y1={40 - Math.max(0, bucket.maximum) * 35} y2={40 - Math.min(0, bucket.minimum) * 35} className="timeline-wave-sample" />;
            })}
            {analysis.beats.map((beat, index) => <line key={`b-${index}`} x1={(beat.timeSeconds / duration) * 1000} x2={(beat.timeSeconds / duration) * 1000} y1="0" y2="80" className="timeline-beat" opacity={0.12 + beat.confidence * 0.28} />)}
            {analysis.bars.map((bar) => <line key={`bar-${bar.number}`} x1={(bar.timeSeconds / duration) * 1000} x2={(bar.timeSeconds / duration) * 1000} y1="0" y2="80" className="timeline-bar" opacity={0.35 + bar.confidence * 0.45} />)}
          </svg>
        </div>
      </div>

      <div className="timeline-lane structure-lane">
        <span className="lane-label">Structure</span>
        <div className="lane-content lane-absolute">
          {analysis.sections.map((section) => <span key={section.id} className={`analysis-segment section-${section.kind.toLowerCase()}`} style={segmentStyle(section.startSeconds, section.endSeconds, duration)} title={`${section.label} · ${section.kind}`}>{section.label}</span>)}
          {analysis.phrases.map((phrase) => <i key={phrase.number} className="phrase-marker" style={{ left: `${pct(phrase.startSeconds, duration)}%` }} title={`Phrase ${phrase.number}`} />)}
        </div>
      </div>

      <div className="timeline-lane lyric-lane">
        <span className="lane-label">Lyrics</span>
        <div className="lane-content lane-absolute">
          {timedLyrics.length === 0 ? <span className="lane-empty">No lyric timing for the current analysis version.</span> : timedLyrics.map((line) => <span key={line.lineNumber} className="lyric-segment" style={segmentStyle(line.startSeconds ?? 0, line.endSeconds ?? line.startSeconds ?? 0, duration)} title={line.text}>{line.text}</span>)}
        </div>
      </div>
    </>
  );
}

function pct(value: number, duration: number): number {
  return Math.max(0, Math.min(100, value / duration * 100));
}

function segmentStyle(start: number, end: number, duration: number) {
  return {
    left: `${pct(start, duration)}%`,
    width: `${Math.max(0.25, pct(Math.max(0, end - start), duration))}%`,
  };
}
