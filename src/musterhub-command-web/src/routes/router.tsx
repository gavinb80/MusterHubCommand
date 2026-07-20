import { createBrowserRouter, redirect } from "react-router-dom";
import { RootLayout } from "./RootLayout";
import { IncidentsListPage } from "./IncidentsListPage";
import { IncidentDetailPage } from "./IncidentDetailPage";
import { SetupPage } from "./SetupPage";
import { SessionExpiredPage } from "./SessionExpiredPage";
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
    children: [
      { index: true, element: <IncidentsListPage /> },
      { path: "incidents/:id", element: <IncidentDetailPage /> },
      { path: "setup", element: <SetupPage /> },
    ],
  },
  { path: "/session-expired", element: <SessionExpiredPage /> },
]);
