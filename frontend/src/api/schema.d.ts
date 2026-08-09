// Generated contract snapshot for the current API.
// Regenerate with `npm run api:generate` while the backend is running.

export interface paths {
  "/api/system/version": {
    get: {
      responses: {
        200: { content: { "application/json": components["schemas"]["SystemVersionResponse"] } };
      };
    };
  };
  "/api/projects/": {
    get: {
      responses: {
        200: { content: { "application/json": components["schemas"]["ProjectResponse"][] } };
      };
    };
    post: {
      requestBody: { content: { "application/json": components["schemas"]["ProjectUpsertRequest"] } };
      responses: {
        201: { content: { "application/json": components["schemas"]["ProjectResponse"] } };
      };
    };
  };
  "/api/projects/{id}": {
    get: {
      parameters: { path: { id: string } };
      responses: {
        200: { content: { "application/json": components["schemas"]["ProjectResponse"] } };
        404: { content?: never };
      };
    };
    put: {
      parameters: { path: { id: string } };
      requestBody: { content: { "application/json": components["schemas"]["ProjectUpsertRequest"] } };
      responses: {
        200: { content: { "application/json": components["schemas"]["ProjectResponse"] } };
        404: { content?: never };
      };
    };
    delete: {
      parameters: { path: { id: string } };
      responses: {
        204: { content?: never };
        404: { content?: never };
      };
    };
  };
  "/api/projects/{id}/export": {
    get: {
      parameters: { path: { id: string } };
      responses: {
        200: { content: { "application/json": unknown } };
        404: { content?: never };
      };
    };
  };
  "/api/projects/import": {
    post: {
      requestBody: { content: { "application/json": unknown } };
      responses: {
        201: { content: { "application/json": components["schemas"]["ProjectResponse"] } };
      };
    };
  };
  "/api/providers/": {
    get: {
      responses: {
        200: { content: { "application/json": components["schemas"]["ProviderCatalogResponse"][] } };
      };
    };
  };
  "/api/providers/{providerId}/settings": {
    get: {
      parameters: { path: { providerId: string } };
      responses: {
        200: { content: { "application/json": components["schemas"]["ProviderSettingsResponse"] } };
        404: { content?: never };
      };
    };
    put: {
      parameters: { path: { providerId: string } };
      requestBody: { content: { "application/json": components["schemas"]["ProviderSettingsRequest"] } };
      responses: {
        200: { content: { "application/json": components["schemas"]["ProviderSettingsResponse"] } };
        400: { content?: unknown };
        404: { content?: never };
      };
    };
  };
  "/api/jobs/": {
    get: {
      responses: {
        200: { content: { "application/json": components["schemas"]["JobResponse"][] } };
      };
    };
    post: {
      requestBody: { content: { "application/json": components["schemas"]["JobCreateRequest"] } };
      responses: {
        201: { content: { "application/json": components["schemas"]["JobResponse"] } };
        400: { content?: unknown };
      };
    };
  };
  "/api/jobs/{id}": {
    get: {
      parameters: { path: { id: string } };
      responses: {
        200: { content: { "application/json": components["schemas"]["JobResponse"] } };
        404: { content?: never };
      };
    };
  };
  "/api/jobs/{id}/attempts": {
    get: {
      parameters: { path: { id: string } };
      responses: {
        200: { content: { "application/json": components["schemas"]["JobAttemptResponse"][] } };
        404: { content?: never };
      };
    };
  };
  "/api/jobs/{id}/dependencies": {
    get: {
      parameters: { path: { id: string } };
      responses: {
        200: { content: { "application/json": string[] } };
        404: { content?: never };
      };
    };
  };
  "/api/jobs/{id}/pause": JobActionPath;
  "/api/jobs/{id}/resume": JobActionPath;
  "/api/jobs/{id}/retry": JobActionPath;
  "/api/jobs/{id}/restart": JobActionPath;
  "/api/jobs/{id}/cancel": JobActionPath;
  "/api/jobs/projects/{projectId}/pause": JobProjectActionPath;
  "/api/jobs/projects/{projectId}/resume": JobProjectActionPath;
  "/api/jobs/projects/{projectId}/cancel": JobProjectActionPath;
  "/api/jobs/projects/{projectId}/scenes/{sceneId}/pause": JobSceneActionPath;
  "/api/jobs/projects/{projectId}/scenes/{sceneId}/resume": JobSceneActionPath;
  "/api/jobs/projects/{projectId}/scenes/{sceneId}/cancel": JobSceneActionPath;
  "/api/jobs/events": {
    get: {
      responses: {
        200: { content: { "text/event-stream": string } };
      };
    };
  };
}

interface JobActionPath {
  post: {
    parameters: { path: { id: string } };
    responses: {
      200: { content?: unknown };
      404: { content?: never };
      409: { content?: never };
    };
  };
}

interface JobProjectActionPath {
  post: {
    parameters: { path: { projectId: string } };
    responses: {
      200: { content: { "application/json": components["schemas"]["JobScopeActionResponse"] } };
    };
  };
}

interface JobSceneActionPath {
  post: {
    parameters: { path: { projectId: string; sceneId: string } };
    responses: {
      200: { content: { "application/json": components["schemas"]["JobScopeActionResponse"] } };
    };
  };
}

export interface components {
  schemas: {
    SystemVersionResponse: {
      applicationName: string;
      version: string;
      environment: string;
    };
    ProjectAspectRatio: "Landscape16x9" | "Portrait9x16" | "Square1x1";
    GenerationPreset: "Fast" | "Balanced" | "BestQuality" | "Cheapest" | "Custom";
    ProjectReferenceKind: "Character" | "Style" | "Location" | "AdditionalMedia";
    ProjectReferenceRequest: {
      kind: components["schemas"]["ProjectReferenceKind"];
      referenceId: string;
    };
    ProjectUpsertRequest: {
      title: string;
      artist: string;
      lyrics: string;
      storyline: string;
      meaning: string;
      visualDirection: string;
      mood: string;
      genre: string;
      aspectRatio: components["schemas"]["ProjectAspectRatio"];
      resolutionWidth: number;
      resolutionHeight: number;
      targetPlatforms?: string[] | null;
      preset: components["schemas"]["GenerationPreset"];
      estimatedBudget?: number | null;
      maximumBudget?: number | null;
      references?: components["schemas"]["ProjectReferenceRequest"][] | null;
    };
    ProjectReferenceResponse: {
      kind: components["schemas"]["ProjectReferenceKind"];
      referenceId: string;
    };
    ProjectResponse: {
      id: string;
      title: string;
      artist: string;
      lyrics: string;
      storyline: string;
      meaning: string;
      visualDirection: string;
      mood: string;
      genre: string;
      aspectRatio: components["schemas"]["ProjectAspectRatio"];
      resolutionWidth: number;
      resolutionHeight: number;
      targetPlatforms: string[];
      preset: components["schemas"]["GenerationPreset"];
      estimatedBudget?: number | null;
      maximumBudget?: number | null;
      references: components["schemas"]["ProjectReferenceResponse"][];
      createdUtc: string;
      updatedUtc: string;
    };
    ProviderCapability:
      | "TextGeneration"
      | "ImageGeneration"
      | "ImageEditing"
      | "VideoGeneration"
      | "ImageToVideo"
      | "VideoToVideo"
      | "LipSync"
      | "Upscale"
      | "Transcription"
      | "VisionEvaluation"
      | "DirectorPlanning";
    CredentialReferenceKind: "Environment" | "OperatingSystem" | "External";
    CredentialReference: {
      kind: components["schemas"]["CredentialReferenceKind"];
      identifier: string;
    };
    ProviderModelResponse: {
      modelId: string;
      displayName: string;
      capabilities: components["schemas"]["ProviderCapability"][];
      supportsReferences: boolean;
      supportsStartFrame: boolean;
      supportsEndFrame: boolean;
      supportsSeed: boolean;
      supportsNegativePrompt: boolean;
      supportsNativeAudio: boolean;
      maxReferences: number;
      supportedDurationsSeconds: number[];
      supportedAspectRatios: string[];
      supportedResolutions: string[];
    };
    ProviderSettingsResponse: {
      providerId: string;
      enabled: boolean;
      credentialReference?: components["schemas"]["CredentialReference"] | null;
      defaultModels: Partial<Record<components["schemas"]["ProviderCapability"], string>>;
      maxConcurrency: number;
      timeoutSeconds: number;
      maxRetries: number;
      allowedOperations: components["schemas"]["ProviderCapability"][];
      priority: number;
      fallbackPriority: number;
    };
    ProviderSettingsRequest: {
      enabled: boolean;
      credentialReference?: components["schemas"]["CredentialReference"] | null;
      defaultModels: Partial<Record<components["schemas"]["ProviderCapability"], string>>;
      maxConcurrency: number;
      timeoutSeconds: number;
      maxRetries: number;
      allowedOperations: components["schemas"]["ProviderCapability"][];
      priority: number;
      fallbackPriority: number;
    };
    ProviderCatalogResponse: {
      id: string;
      displayName: string;
      models: components["schemas"]["ProviderModelResponse"][];
      settings: components["schemas"]["ProviderSettingsResponse"];
    };
    JobState:
      | "Draft"
      | "Queued"
      | "Submitting"
      | "ProviderQueued"
      | "Generating"
      | "Downloading"
      | "Validating"
      | "Completed"
      | "Paused"
      | "WaitingForQuota"
      | "WaitingForProvider"
      | "WaitingForDependency"
      | "RetryScheduled"
      | "Rejected"
      | "FailedRetryable"
      | "FailedPermanent"
      | "Cancelled";
    JobCreateRequest: {
      projectId?: string | null;
      sceneId?: string | null;
      parentJobId?: string | null;
      type: string;
      payloadJson: string;
      providerId?: string | null;
      modelId?: string | null;
      priority: number;
      maxRetries: number;
      estimatedCost?: number | null;
      currency?: string | null;
      dependencies?: string[] | null;
    };
    JobResponse: {
      id: string;
      projectId?: string | null;
      sceneId?: string | null;
      parentJobId?: string | null;
      type: string;
      providerId?: string | null;
      modelId?: string | null;
      state: components["schemas"]["JobState"];
      resumeState?: components["schemas"]["JobState"] | null;
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
    };
    JobAttemptResponse: {
      attemptNumber: number;
      startedUtc: string;
      completedUtc?: string | null;
      state: components["schemas"]["JobState"];
      providerTaskId?: string | null;
      errorCode?: string | null;
      errorMessage?: string | null;
      estimatedCost?: number | null;
      actualCost?: number | null;
      currency?: string | null;
    };
    JobScopeActionResponse: {
      affectedJobs: number;
    };
  };
}
