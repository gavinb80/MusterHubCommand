// Cross-app handoff, same convention as Rota/Skills' own token stores: core
// hands off into Command with a short-lived access token in the URL
// *fragment* (never a query string, so it never reaches server access
// logs). This module pulls it out of the URL on first load, persists it for
// the tab's lifetime, and scrubs it from the visible URL/history immediately.

const STORAGE_KEY = "musterhub-command:access-token";

function readTokenFromLocation(): string | null {
  const hash = window.location.hash;
  if (!hash.startsWith("#")) return null;

  const params = new URLSearchParams(hash.slice(1));
  const token = params.get("token");
  if (!token) return null;

  params.delete("token");
  const rest = params.toString();
  const newUrl =
    window.location.pathname + window.location.search + (rest ? `#${rest}` : "");
  window.history.replaceState(null, "", newUrl);

  return token;
}

export function getStoredToken(): string | null {
  return sessionStorage.getItem(STORAGE_KEY);
}

export function setStoredToken(token: string): void {
  sessionStorage.setItem(STORAGE_KEY, token);
}

export function clearStoredToken(): void {
  sessionStorage.removeItem(STORAGE_KEY);
}

export function resolveAccessToken(): string | null {
  const fromUrl = readTokenFromLocation();
  if (fromUrl) {
    setStoredToken(fromUrl);
    return fromUrl;
  }
  return getStoredToken();
}
