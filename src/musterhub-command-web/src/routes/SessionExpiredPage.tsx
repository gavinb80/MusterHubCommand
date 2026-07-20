const CORE_APP_URL = import.meta.env.VITE_CORE_APP_URL ?? "http://localhost:5005";

export function SessionExpiredPage() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-(--surface-page) px-4">
      <div className="w-full max-w-sm rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card">
        <h1 className="text-page-title font-semibold text-brand-secondary">
          Session expired
        </h1>
        <p className="mt-2 text-body text-(--content-secondary)">
          Your MusterHub Command session has ended. Sign back in through MusterHub
          to continue.
        </p>
        <a
          href={CORE_APP_URL}
          className="mt-4 inline-block rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white"
        >
          Back to MusterHub
        </a>
      </div>
    </div>
  );
}
