import type { paths } from "./schema";

type SystemVersionResponse = paths["/api/system/version"]["get"]["responses"][200]["content"]["application/json"];
type ProviderCatalogResponse = paths["/api/providers/"]["get"]["responses"][200]["content"]["application/json"];

const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";

export async function getSystemVersion(signal?: AbortSignal): Promise<SystemVersionResponse> {
  const response = await fetch(`${apiBaseUrl}/api/system/version`, {
    method: "GET",
    headers: { Accept: "application/json" },
    signal,
  });

  if (!response.ok) {
    throw new Error(`Backend version request failed with HTTP ${response.status}.`);
  }

  return (await response.json()) as SystemVersionResponse;
}

export async function getProviderCatalog(signal?: AbortSignal): Promise<ProviderCatalogResponse> {
  const response = await fetch(`${apiBaseUrl}/api/providers/`, {
    method: "GET",
    headers: { Accept: "application/json" },
    signal,
  });

  if (!response.ok) {
    throw new Error(`Provider catalog request failed with HTTP ${response.status}.`);
  }

  return (await response.json()) as ProviderCatalogResponse;
}
