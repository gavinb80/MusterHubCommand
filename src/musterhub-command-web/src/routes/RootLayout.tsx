import { NavLink, Outlet } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "../auth/apiClient";
import { clearStoredToken } from "../auth/tokenStore";
import type { MeResponse } from "../api/types";

// Same fallback as SessionExpiredPage -- Command has no login of its own.
const CORE_APP_URL = import.meta.env.VITE_CORE_APP_URL ?? "http://localhost:5005";

function AccountBlock({ name }: { name: string | null }) {
  return (
    <div className="flex items-center gap-2 text-body">
      <span
        className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-white/20 text-caption font-semibold text-white"
        aria-hidden="true"
      >
        {name?.trim().charAt(0).toUpperCase() ?? "?"}
      </span>
      <span className="text-white/90">{name ?? "Signed in"}</span>
      <button
        type="button"
        className="text-caption text-white/70 underline hover:text-white"
        onClick={() => {
          clearStoredToken();
          window.location.href = CORE_APP_URL;
        }}
      >
        Sign out
      </button>
    </div>
  );
}

function NavItem({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <NavLink
      to={to}
      end
      className={({ isActive }: { isActive: boolean }) =>
        `text-body ${isActive ? "font-semibold text-white" : "text-white/70 hover:text-white"}`
      }
    >
      {children}
    </NavLink>
  );
}

export function RootLayout() {
  const meQuery = useQuery({ queryKey: ["me"], queryFn: () => apiFetch<MeResponse>("/me") });

  return (
    <div className="flex min-h-screen flex-col bg-(--surface-page)">
      <header className="bg-brand-secondary px-6 py-4">
        <div className="flex items-center justify-between gap-6">
          <span className="text-card-title font-semibold text-white">
            MusterHub <span className="text-brand-primary-dark">Command</span>
          </span>
          <nav className="flex flex-1 items-center gap-4">
            <NavItem to="/">Incidents</NavItem>
            {meQuery.data?.isOperator && <NavItem to="/setup">Setup</NavItem>}
          </nav>
          {meQuery.data && <AccountBlock name={meQuery.data.displayName} />}
        </div>
        {meQuery.data?.isBootstrapping && (
          <p className="mt-3 rounded-lg bg-white/10 px-3 py-2 text-caption text-white/90">
            Nobody has been made a Control Room Operator for this organisation yet, so every
            signed-in user can act as one. Assign the role in Setup to close this off.
          </p>
        )}
      </header>
      <main className="flex-1 overflow-x-hidden px-6 py-6">
        <Outlet />
      </main>
    </div>
  );
}
