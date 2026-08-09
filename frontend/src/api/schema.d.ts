// Generated contract snapshot for the current API.
// Regenerate with `npm run api:generate` while the backend is running.

export interface paths {
  "/api/system/version": {
    get: {
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["SystemVersionResponse"];
          };
        };
      };
    };
  };
  "/api/projects/": {
    get: {
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["ProjectResponse"][];
          };
        };
      };
    };
    post: {
      requestBody: {
        content: {
          "application/json": components["schemas"]["ProjectUpsertRequest"];
        };
      };
      responses: {
        201: {
          content: {
            "application/json": components["schemas"]["ProjectResponse"];
          };
        };
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
        200: {
          content: {
            "application/json": components["schemas"]["ProviderCatalogResponse"][];
          };
        };
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
  };
}
