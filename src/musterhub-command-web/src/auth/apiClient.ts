import { clearStoredToken, getStoredToken } from "./tokenStore";

// Thrown on a 401 so callers (React Query error boundaries, route loaders)
// can distinguish "core issued no/expired token" from a generic request
// failure and bounce the user back through core's login. Same shape as
// Rota/Skills' own apiClient.
export class SessionExpiredError extends Error {
  constructor() {
    super("Your MusterHub Command session has expired.");
    this.name = "SessionExpiredError";
  }
}

export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = getStoredToken();
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);
  // FormData needs the browser to set its own Content-Type (with the
  // multipart boundary) -- setting one here ourselves would drop it.
  if (init.body && !(init.body instanceof FormData) && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(`/api${path}`, { ...init, headers });

  if (response.status === 401) {
    clearStoredToken();
    throw new SessionExpiredError();
  }

  if (!response.ok) {
    const detail = await response.text();
    throw new Error(`Request to ${path} failed (${response.status}): ${detail}`);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

// A plain <img src="..."> or <a href="..."> can't carry a Bearer token, so
// anything reading attachment bytes (thumbnails, downloads, the print
// export) fetches the blob through here and builds its own object URL --
// see IncidentDetailPage's PhotosPanel and IncidentPrintPage.
export async function apiFetchBlob(path: string): Promise<Blob> {
  const token = getStoredToken();
  const headers = new Headers();
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const response = await fetch(`/api${path}`, { headers });

  if (response.status === 401) {
    clearStoredToken();
    throw new SessionExpiredError();
  }
  if (!response.ok) {
    const detail = await response.text();
    throw new Error(`Request to ${path} failed (${response.status}): ${detail}`);
  }
  return await response.blob();
}
