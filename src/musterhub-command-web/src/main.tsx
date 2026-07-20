import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router-dom";
import "./index.css";
import { SessionExpiredError } from "./auth/apiClient";
import { clearStoredToken } from "./auth/tokenStore";
import { router } from "./routes/router";
import { ToastProvider } from "./components/ToastProvider";

// Same global session-expiry handling as Rota/Skills' own main.tsx: every
// query/mutation across the app funnels a 401 through here once, rather
// than each screen handling it individually.
function handleSessionExpiry(error: unknown) {
  if (error instanceof SessionExpiredError) {
    clearStoredToken();
    void router.navigate("/session-expired");
  }
}

function isRetryable(failureCount: number, error: unknown) {
  return !(error instanceof SessionExpiredError) && failureCount < 3;
}

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: isRetryable } },
  queryCache: new QueryCache({ onError: handleSessionExpiry }),
  mutationCache: new MutationCache({ onError: handleSessionExpiry }),
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <RouterProvider router={router} />
      </ToastProvider>
    </QueryClientProvider>
  </StrictMode>,
);
