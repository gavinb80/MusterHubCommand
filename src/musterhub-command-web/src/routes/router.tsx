import { createBrowserRouter, redirect } from "react-router-dom";
import { RootLayout } from "./RootLayout";
import { IncidentsListPage } from "./IncidentsListPage";
import { IncidentDetailPage } from "./IncidentDetailPage";
import { IncidentHierarchyPage } from "./IncidentHierarchyPage";
import { IncidentPrintPage } from "./IncidentPrintPage";
import { SetupPage } from "./SetupPage";
import { SessionExpiredPage } from "./SessionExpiredPage";
import { ErrorBoundaryPage } from "./ErrorBoundaryPage";
import { resolveAccessToken } from "../auth/tokenStore";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <RootLayout />,
    // Same gate as Rota/Skills' own router.tsx -- see their loader comment
    // for why this has to run here rather than in main.tsx.
    loader: () => {
      if (!resolveAccessToken()) throw redirect("/session-expired");
      return null;
    },
    // Catches render/loader/action throws from this route and every child
    // below -- without it, an unhandled throw left the whole screen blank.
    errorElement: <ErrorBoundaryPage />,
    children: [
      { index: true, element: <IncidentsListPage /> },
      { path: "incidents/:id", element: <IncidentDetailPage /> },
      { path: "incidents/:id/hierarchy", element: <IncidentHierarchyPage /> },
      { path: "setup", element: <SetupPage /> },
    ],
  },
  {
    // Deliberately outside RootLayout -- a printed report shouldn't carry
    // the app's nav bar/sign-out chrome onto the page.
    path: "incidents/:id/print",
    element: <IncidentPrintPage />,
    errorElement: <ErrorBoundaryPage />,
    loader: () => {
      if (!resolveAccessToken()) throw redirect("/session-expired");
      return null;
    },
  },
  { path: "/session-expired", element: <SessionExpiredPage /> },
]);
