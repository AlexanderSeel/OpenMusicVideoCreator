export type JobState = "Draft" | "Queued" | "Submitting" | "ProviderQueued" | "Generating" | "Downloading" | "Validating" | "Completed" | "Paused" | "WaitingForQuota" | "WaitingForProvider" | "WaitingForDependency" | "RetryScheduled" | "Rejected" | "FailedRetryable" | "FailedPermanent" | "Cancelled";

export interface JobResponse {
  id: string;
  projectId?: string | null;
  sceneId?: string | null;
  parentJobId?: string | null;
  type: string;
  providerId?: string | null;
  modelId?: string | null;
  state: JobState;
  resumeState?: JobState | null;
  priority: number;
  attemptCount: number;
  retryCount: number;
  maxRetries: number;
  createdUtc: string;
  updatedUtc: string;
  nextRunUtc?: string | null;
  startedUtc?: string | null;
  completedUtc?: string | null;
  providerTaskId?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
  estimatedCost?: number | null;
  actualCost?: number | null;
  currency?: string | null;
}

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";

async function readJson<T>(response: Response, context: string): Promise<T> {
  if (!response.ok) throw new Error(`${context} failed with HTTP ${response.status}.`);
  return (await response.json()) as T;
}

export async function listJobs(signal?: AbortSignal): Promise<JobResponse[]> {
  return readJson<JobResponse[]>(await fetch(`${apiBaseUrl}/api/jobs/`, { headers: { Accept: "application/json" }, signal }), "Job list");
}

async function jobAction(jobId: string, action: "pause" | "resume" | "retry" | "restart" | "cancel"): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/jobs/${jobId}/${action}`, { method: "POST" });
  if (!response.ok) throw new Error(`${action} job failed with HTTP ${response.status}.`);
}

async function scopeAction(path: string): Promise<number> {
  return (await readJson<{ affectedJobs: number }>(await fetch(`${apiBaseUrl}${path}`, { method: "POST" }), "Job scope action")).affectedJobs;
}

export const pauseJob = (id: string) => jobAction(id, "pause");
export const resumeJob = (id: string) => jobAction(id, "resume");
export const retryJob = (id: string) => jobAction(id, "retry");
export const restartJob = (id: string) => jobAction(id, "restart");
export const cancelJob = (id: string) => jobAction(id, "cancel");
export const pauseProjectJobs = (projectId: string) => scopeAction(`/api/jobs/projects/${projectId}/pause`);
export const resumeProjectJobs = (projectId: string) => scopeAction(`/api/jobs/projects/${projectId}/resume`);
export const cancelProjectJobs = (projectId: string) => scopeAction(`/api/jobs/projects/${projectId}/cancel`);
export const pauseSceneJobs = (projectId: string, sceneId: string) => scopeAction(`/api/jobs/projects/${projectId}/scenes/${sceneId}/pause`);
export const resumeSceneJobs = (projectId: string, sceneId: string) => scopeAction(`/api/jobs/projects/${projectId}/scenes/${sceneId}/resume`);
export const cancelSceneJobs = (projectId: string, sceneId: string) => scopeAction(`/api/jobs/projects/${projectId}/scenes/${sceneId}/cancel`);

export function jobEventsUrl(): string {
  return `${apiBaseUrl}/api/jobs/events`;
}
