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
  if (init.body && !headers.has("Content-Type")) {
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
