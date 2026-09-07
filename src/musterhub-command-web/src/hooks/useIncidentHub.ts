import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { getStoredToken } from "../auth/tokenStore";

// A cache-invalidation nudge, not a data pipe -- matches the API's own
// IncidentHub comment. Every connected client auto-joins its org's group
// (see IncidentHub.OnConnectedAsync), so "incidents" is always worth
// invalidating on a signal; "incident-{incidentId}" is the narrower group
// this hook also joins when a specific incident's detail page is open, so
// that query gets invalidated too. incidentId is optional: the incidents
// list page only wants the org-wide signal.
//
// A long refetchInterval stays configured on the underlying useQuery calls
// as a fallback (see IncidentDetailPage/IncidentsListPage) -- if the socket
// drops silently, that poll is the safety net, not a redundant primary path.
export function useIncidentHub(organisationId: string | undefined, incidentId?: string) {
  const queryClient = useQueryClient();
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    if (!organisationId) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/incidents", { accessTokenFactory: () => getStoredToken() ?? "" })
      .withAutomaticReconnect()
      .build();

    connection.on("IncidentUpdated", (updatedIncidentId: string) => {
      queryClient.invalidateQueries({ queryKey: ["incidents"] });
      if (incidentId && updatedIncidentId === incidentId) {
        queryClient.invalidateQueries({ queryKey: ["incident", incidentId] });
      }
    });

    connection.onreconnected(() => setConnected(true));
    connection.onreconnecting(() => setConnected(false));
    connection.onclose(() => setConnected(false));

    connection.start()
      .then(() => {
        setConnected(true);
        if (incidentId) return connection.invoke("JoinIncident", incidentId);
      })
      .catch(() => setConnected(false));

    return () => {
      if (incidentId) connection.invoke("LeaveIncident", incidentId).catch(() => {});
      connection.stop();
    };
  }, [organisationId, incidentId, queryClient]);

  return connected;
}
