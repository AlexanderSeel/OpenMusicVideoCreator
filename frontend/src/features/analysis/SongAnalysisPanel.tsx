"use client";

import { useEffect, useMemo, useState } from "react";
import {
  analyzeSong,
  getLyricTiming,
  getSongAnalysis,
  updateSongAnalysisSections,
  type LyricTimingResponse,
  type SongAnalysisResponse,
  type SongSectionKind,
  type SongSectionRequest,
} from "@/src/api/client";

interface SongAnalysisPanelProps {
  projectId?: string;
  songAttached: boolean;
  lyrics: string;
}

const sectionKinds: SongSectionKind[] = [
  "Unknown", "Intro", "Verse", "PreChorus", "Chorus", "Bridge", "Breakdown", "Instrumental", "Outro",
];

export function SongAnalysisPanel({ projectId, songAttached, lyrics }: SongAnalysisPanelProps) {
  const [analysis, setAnalysis] = useState<SongAnalysisResponse | null>(null);
  const [lyricTiming, setLyricTiming] = useState<LyricTimingResponse | null>(null);
  const [sections, setSections] = useState<SongSectionRequest[]>([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    if (!projectId) {
      setAnalysis(null);
      setLyricTiming(null);
      setSections([]);
      setMessage("");
      return () => controller.abort();
    }

    setLoading(true);
    setMessage("");
    Promise.all([
      getSongAnalysis(projectId, controller.signal),
      getLyricTiming(projectId, controller.signal),
    ])
      .then(([result, timing]) => {
        setAnalysis(result);
        setLyricTiming(timing);
        setSections(result ? toSectionRequests(result) : []);
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setMessage(error instanceof Error ? error.message : "Could not load song analysis.");
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });

    return () => controller.abort();
  }, [projectId]);

  const sectionDuration = useMemo(
    () => sections.reduce((total, section) => total + Math.max(0, section.endSeconds - section.startSeconds), 0),
    [sections],
  );

  async function runAnalysis() {
    if (!projectId || !songAttached || loading) return;
    setLoading(true);
    setMessage("");
    try {
      const result = await analyzeSong(projectId);
      setAnalysis(result);
      setSections(toSectionRequests(result));
      setLyricTiming(null);
      setMessage(`Analysis version ${result.version} created. Existing lyric timing is treated as stale until re-aligned.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Song analysis failed.");
    } finally {
      setLoading(false);
    }
  }

  async function saveSections() {
    if (!projectId || !analysis || saving) return;
    setSaving(true);
    setMessage("");
    try {
      const result = await updateSongAnalysisSections(projectId, sections);
      setAnalysis(result);
      setSections(toSectionRequests(result));
      setLyricTiming(null);
      setMessage(`Structure Map saved as version ${result.version}. Lyric timing can be re-aligned against this version.`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Could not save Structure Map.");
    } finally {
      setSaving(false);
    }
  }

  function updateSection<K extends keyof SongSectionRequest>(index: number, key: K, value: SongSectionRequest[K]) {
    setSections((current) => current.map((section, currentIndex) =>
      currentIndex === index ? { ...section, [key]: value } : section));
  }

  if (!projectId) {
    return (
      <section className="analysis-panel analysis-empty" aria-labelledby="analysis-heading">
        <AnalysisHeader />
        <p>Save the project first, then attach a song to unlock waveform and Structure Map analysis.</p>
      </section>
    );
  }

  const timingMatchesCurrentAnalysis = Boolean(
    analysis && lyricTiming && lyricTiming.songAnalysisId === analysis.id,
  );

  return (
    <section className="analysis-panel" aria-labelledby="analysis-heading">
      <AnalysisHeader />
      {!songAttached ? (
        <div className="analysis-callout">
          <div><strong>No song attached</strong><span>Choose a song above and save the project before analysis.</span></div>
        </div>
      ) : null}

      <div className="analysis-toolbar">
        <div>
          <strong>{analysis ? `Analysis v${analysis.version}` : "Not analyzed yet"}</strong>
          <span>{analysis ? "Local FFmpeg/ffprobe analysis is persisted and editable." : "Analyze locally before any AI Director work."}</span>
        </div>
        <button className="button button-primary" type="button" disabled={!songAttached || loading} onClick={() => void runAnalysis()}>
          {loading ? "Analyzing…" : analysis ? "Analyze again" : "Analyze song"}
        </button>
      </div>

      {message ? <div className="status-banner" role="status">{message}</div> : null}

      {analysis ? (
        <>
          <div className="analysis-stats" aria-label="Song analysis summary">
            <Stat label="Duration" value={formatDuration(analysis.durationSeconds)} />
            <Stat label="BPM" value={analysis.bpm ? analysis.bpm.toFixed(1) : "uncertain"} />
            <Stat label="Sample rate" value={analysis.sampleRate ? `${analysis.sampleRate.toLocaleString()} Hz` : "—"} />
            <Stat label="Beats" value={analysis.beats.length.toString()} />
            <Stat label="Bars" value={analysis.bars.length.toString()} />
            <Stat label="Phrases" value={analysis.phrases.length.toString()} />
            <Stat label="Quiet ranges" value={analysis.quietRanges.length.toString()} />
            <Stat label="Likely vocal" value={analysis.vocalActivity ? formatPercent(analysis.vocalActivity.vocalFraction) : "uncertain"} />
            <Stat label="Likely instrumental" value={analysis.vocalActivity ? formatPercent(analysis.vocalActivity.instrumentalFraction) : "uncertain"} />
          </div>

          <div className="waveform-card">
            <div className="waveform-heading">
              <strong>Waveform, beats, bars & phrases</strong>
              <span>{analysis.codec ?? "audio"} • {analysis.channels ?? "?"} ch</span>
            </div>
            <Waveform analysis={analysis} />
            <div className="timeline-scale" aria-label="Waveform legend">
              <span>beat · bar</span><span>phrase band</span><span>quiet shading</span>
            </div>
            <div className="timeline-scale" aria-hidden="true">
              <span>0:00</span><span>{formatDuration(analysis.durationSeconds / 2)}</span><span>{formatDuration(analysis.durationSeconds)}</span>
            </div>
            <div className="lyrics-lane">
              <span>Lyrics lane · authoritative text</span>
              <p>{lyrics.trim() || "No supplied lyrics yet."}</p>
              {timingMatchesCurrentAnalysis && lyricTiming ? (
                <div className="lyric-timing-summary" aria-label="Transcription assisted lyric timing">
                  <strong>Timing v{lyricTiming.version} · {formatPercent(lyricTiming.matchedFraction)} matched</strong>
                  <span>Transcription only suggests timestamps; the lyric text above is never replaced.</span>
                  <ul>
                    {lyricTiming.lines.filter((line) => line.isMatched).slice(0, 8).map((line) => (
                      <li key={line.lineNumber}>
                        <time>{formatDuration(line.startSeconds ?? 0)}</time>
                        <span>{line.text}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              ) : (
                <small>Optional transcription timing is not available for this analysis version yet.</small>
              )}
            </div>
          </div>

          <div className="structure-map">
            <div className="structure-heading">
              <div>
                <strong>Structure Map</strong>
                <span>Detected boundaries are suggestions. Edits create a new analysis version.</span>
              </div>
              <div className="structure-summary">
                <span>{sections.length} sections</span><span>{formatDuration(sectionDuration)} mapped</span>
              </div>
            </div>

            <div className="section-table" role="group" aria-label="Editable song sections">
              <div className="section-row section-row-head" aria-hidden="true">
                <span>Section</span><span>Type</span><span>Start</span><span>End</span><span>Source</span>
              </div>
              {sections.map((section, index) => (
                <div className="section-row" key={section.id ?? `${section.startSeconds}-${index}`}>
                  <input aria-label={`Section ${index + 1} label`} value={section.label} onChange={(event) => updateSection(index, "label", event.target.value)} />
                  <select aria-label={`Section ${index + 1} type`} value={section.kind} onChange={(event) => updateSection(index, "kind", event.target.value as SongSectionKind)}>
                    {sectionKinds.map((kind) => <option key={kind} value={kind}>{formatKind(kind)}</option>)}
                  </select>
                  <input aria-label={`Section ${index + 1} start in seconds`} type="number" min="0" max={analysis.durationSeconds} step="0.1" value={section.startSeconds} onChange={(event) => updateSection(index, "startSeconds", Number(event.target.value))} />
                  <input aria-label={`Section ${index + 1} end in seconds`} type="number" min="0" max={analysis.durationSeconds} step="0.1" value={section.endSeconds} onChange={(event) => updateSection(index, "endSeconds", Number(event.target.value))} />
                  <span className="source-pill">{analysis.sections[index]?.source === "UserEdited" ? "edited" : "detected"}</span>
                </div>
              ))}
            </div>

            <div className="structure-actions">
              <span>Ranges must be ordered, non-overlapping, and inside the song duration.</span>
              <button className="button" type="button" disabled={saving} onClick={() => void saveSections()}>{saving ? "Saving…" : "Save Structure Map"}</button>
            </div>
          </div>
        </>
      ) : null}
    </section>
  );
}

function AnalysisHeader() {
  return <div className="section-heading analysis-title"><div><span>05</span><h2 id="analysis-heading">Song analysis</h2></div><p>Local signal analysis drives timing before storyboard generation.</p></div>;
}

function Stat({ label, value }: { label: string; value: string }) {
  return <div className="analysis-stat"><span>{label}</span><strong>{value}</strong></div>;
}

function Waveform({ analysis }: { analysis: SongAnalysisResponse }) {
  const width = 1000;
  const height = 150;
  const middle = height / 2;
  const duration = Math.max(analysis.durationSeconds, 0.001);
  return (
    <svg className="waveform" viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Song waveform with detected beats, bars, phrases, and quiet ranges" preserveAspectRatio="none">
      {analysis.quietRanges.map((range, index) => (
        <rect key={`quiet-${index}`} x={(range.startSeconds / duration) * width} y="0" width={Math.max(1, ((range.endSeconds - range.startSeconds) / duration) * width)} height={height} fill="rgba(110,231,183,0.08)" />
      ))}
      {analysis.phrases.map((phrase) => (
        <rect key={`phrase-${phrase.number}`} x={(phrase.startSeconds / duration) * width} y="0" width={Math.max(1, ((phrase.endSeconds - phrase.startSeconds) / duration) * width)} height="8" fill="rgba(102,212,255,0.45)" opacity={0.25 + phrase.confidence * 0.5} />
      ))}
      <line className="waveform-zero" x1="0" x2={width} y1={middle} y2={middle} />
      {analysis.waveform.map((bucket, index) => {
        const x = ((bucket.startSeconds + bucket.endSeconds) / 2 / duration) * width;
        const y1 = middle - Math.max(0, bucket.maximum) * middle * 0.9;
        const y2 = middle - Math.min(0, bucket.minimum) * middle * 0.9;
        return <line key={index} className="waveform-sample" x1={x} x2={x} y1={y1} y2={y2} />;
      })}
      {analysis.beats.map((beat, index) => <line key={`beat-${index}`} className="beat-marker" x1={(beat.timeSeconds / duration) * width} x2={(beat.timeSeconds / duration) * width} y1="10" y2={height} opacity={0.12 + beat.confidence * 0.28} />)}
      {analysis.bars.map((bar) => <line key={`bar-${bar.number}`} x1={(bar.timeSeconds / duration) * width} x2={(bar.timeSeconds / duration) * width} y1="8" y2={height} stroke="rgba(255,211,122,0.78)" strokeWidth="1.5" opacity={0.3 + bar.confidence * 0.5} />)}
    </svg>
  );
}

function toSectionRequests(analysis: SongAnalysisResponse): SongSectionRequest[] {
  return analysis.sections.map((section) => ({ id: section.id, label: section.label, kind: section.kind, startSeconds: round(section.startSeconds), endSeconds: round(section.endSeconds) }));
}

function round(value: number) { return Math.round(value * 10) / 10; }
function formatDuration(seconds: number): string { const safe = Math.max(0, Math.round(seconds)); return `${Math.floor(safe / 60)}:${String(safe % 60).padStart(2, "0")}`; }
function formatKind(kind: SongSectionKind): string { return kind.replace(/([a-z])([A-Z])/g, "$1 $2"); }
function formatPercent(value: number): string { return `${Math.round(Math.max(0, Math.min(1, value)) * 100)}%`; }
