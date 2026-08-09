// Generated contract snapshot for the bootstrap API.
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
}

export interface components {
  schemas: {
    SystemVersionResponse: {
      applicationName: string;
      version: string;
      environment: string;
    };
  };
}
