import { isRouteErrorResponse, useRouteError, useNavigate } from "react-router-dom";

// react-router's errorElement doubles as this app's error boundary: it
// catches render-time throws, loader errors, and action errors for whatever
// route it's attached to, so one component covers all three without a
// separate class-based ErrorBoundary. Without this, an unhandled throw (a
// null-pointer on an unexpected API shape, a Leaflet map error) left the
// operator's whole screen blank with no recovery but a manual reload -- a
// worse failure mode here than in most apps, mid-incident.
export function ErrorBoundaryPage() {
  const error = useRouteError();
  const navigate = useNavigate();

  console.error("Unhandled route error:", error);

  const message = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : error instanceof Error
      ? error.message
      : "Something went wrong.";

  return (
    <div className="flex min-h-screen items-center justify-center bg-(--surface-page) px-4">
      <div className="w-full max-w-sm rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card">
        <h1 className="text-page-title font-semibold text-brand-secondary">
          Something went wrong
        </h1>
        <p className="mt-2 text-body text-(--content-secondary)">
          This screen hit a problem and couldn't continue. Your other incident
          data is unaffected.
        </p>
        <p className="mt-2 text-caption text-(--content-secondary)">{message}</p>
        <div className="mt-4 flex gap-2">
          <button
            type="button"
            onClick={() => window.location.reload()}
            className="rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white"
          >
            Reload
          </button>
          <button
            type="button"
            onClick={() => navigate("/")}
            className="rounded-lg border border-(--surface-border) px-4 py-2 text-body font-semibold text-(--content-secondary)"
          >
            Back to incidents
          </button>
        </div>
      </div>
    </div>
  );
}
