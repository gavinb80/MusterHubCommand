import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "../auth/apiClient";
import { useToast } from "../components/ToastProvider";
import { IncidentMap } from "../components/IncidentMap";
import type {
  AddIncidentUpdateRequest, ApplianceStatus, DeviceDto, IncidentDto, IncidentUpdateType,
  RouteResponseDto, SetApplianceEntry,
} from "../api/types";

const APPLIANCE_STATUSES: ApplianceStatus[] = ["Mobilised", "EnRoute", "OnScene", "StoodDown"];
const UPDATE_TYPES: IncidentUpdateType[] = ["General", "Hazard", "ResourceChange", "Note"];

const APPLIANCE_STATUS_STYLES: Record<ApplianceStatus, string> = {
  Mobilised: "bg-status-mobilised/15 text-status-mobilised",
  EnRoute: "bg-status-en-route/15 text-status-en-route",
  OnScene: "bg-status-on-scene/15 text-status-on-scene",
  StoodDown: "bg-status-stood-down/15 text-status-stood-down",
};

function AttendancePanel({ incident }: { incident: IncidentDto }) {
  const [editing, setEditing] = useState(false);
  const [rows, setRows] = useState<SetApplianceEntry[]>([]);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const startEditing = () => {
    setRows(incident.appliances.map((a) => ({ callsign: a.callsign, status: a.status })));
    setEditing(true);
  };

  const setAppliancesMutation = useMutation({
    mutationFn: (appliances: SetApplianceEntry[]) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/appliances`, {
        method: "PUT",
        body: JSON.stringify({ appliances }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incident", incident.id] });
      showToast("Attendance updated");
      setEditing(false);
    },
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <div className="rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card">
      <div className="flex items-center justify-between">
        <h2 className="text-card-title font-semibold text-(--content-primary)">Attendance</h2>
        {!editing && (
          <button type="button" onClick={startEditing} className="text-body text-brand-primary">
            Edit
          </button>
        )}
      </div>

      {!editing && (
        <div className="mt-3 flex flex-col gap-2">
          {incident.appliances.length === 0 && (
            <p className="text-body text-(--content-secondary)">No appliances attending yet.</p>
          )}
          {incident.appliances.map((a) => (
            <div key={a.id} className="flex items-center justify-between">
              <span className="text-body font-medium text-(--content-primary)">{a.callsign}</span>
              <span className={`rounded-full px-2 py-0.5 text-caption font-semibold ${APPLIANCE_STATUS_STYLES[a.status]}`}>
                {a.status}
              </span>
            </div>
          ))}
        </div>
      )}

      {editing && (
        <div className="mt-3 flex flex-col gap-2">
          {rows.map((row, i) => (
            <div key={i} className="flex items-center gap-2">
              <input
                value={row.callsign}
                onChange={(e) => setRows(rows.map((r, j) => (j === i ? { ...r, callsign: e.target.value } : r)))}
                placeholder="Callsign"
                className="flex-1 rounded-lg border border-(--surface-border) px-2 py-1 text-body"
              />
              <select
                value={row.status}
                onChange={(e) => setRows(rows.map((r, j) => (j === i ? { ...r, status: e.target.value as ApplianceStatus } : r)))}
                className="rounded-lg border border-(--surface-border) px-2 py-1 text-body"
              >
                {APPLIANCE_STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
              <button type="button" onClick={() => setRows(rows.filter((_, j) => j !== i))} className="text-caption text-(--content-secondary)">
                Remove
              </button>
            </div>
          ))}
          <button
            type="button"
            onClick={() => setRows([...rows, { callsign: "", status: "Mobilised" }])}
            className="self-start text-body text-brand-primary"
          >
            + Add appliance
          </button>
          <div className="mt-2 flex justify-end gap-2">
            <button type="button" onClick={() => setEditing(false)} className="rounded-lg px-3 py-1.5 text-body text-(--content-secondary)">
              Cancel
            </button>
            <button
              type="button"
              disabled={setAppliancesMutation.isPending}
              onClick={() => setAppliancesMutation.mutate(rows.filter((r) => r.callsign.trim() !== ""))}
              className="rounded-lg bg-brand-primary px-3 py-1.5 text-body font-semibold text-white disabled:opacity-60"
            >
              Save
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function TimelinePanel({ incident }: { incident: IncidentDto }) {
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const addUpdateMutation = useMutation({
    mutationFn: (request: AddIncidentUpdateRequest) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/updates`, { method: "POST", body: JSON.stringify(request) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incident", incident.id] });
      showToast("Update added");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const sortedUpdates = [...incident.updates].sort(
    (a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime(),
  );

  return (
    <div className="rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card">
      <h2 className="text-card-title font-semibold text-(--content-primary)">Timeline</h2>

      <form
        className="mt-3 flex flex-col gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          const form = new FormData(e.currentTarget);
          const text = String(form.get("text") || "").trim();
          if (!text) return;
          addUpdateMutation.mutate({ text, updateType: form.get("updateType") as IncidentUpdateType });
          e.currentTarget.reset();
        }}
      >
        <div className="flex gap-2">
          <input
            name="text"
            required
            placeholder="Add an update..."
            className="flex-1 rounded-lg border border-(--surface-border) px-3 py-2 text-body"
          />
          <select name="updateType" defaultValue="General" className="rounded-lg border border-(--surface-border) px-2 py-2 text-body">
            {UPDATE_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
          <button
            type="submit"
            disabled={addUpdateMutation.isPending}
            className="rounded-lg bg-brand-primary px-3 py-2 text-body font-semibold text-white disabled:opacity-60"
          >
            Post
          </button>
        </div>
      </form>

      <div className="mt-4 flex flex-col gap-3">
        {sortedUpdates.length === 0 && <p className="text-body text-(--content-secondary)">No updates yet.</p>}
        {sortedUpdates.map((u) => (
          <div
            key={u.id}
            className={`rounded-lg border p-3 ${u.updateType === "Hazard" ? "border-status-hazard/40 bg-status-hazard/10" : "border-(--surface-border)"}`}
          >
            <div className="flex items-center justify-between text-caption text-(--content-secondary)">
              <span>
                {u.updateType === "Hazard" && <span className="mr-1 font-semibold text-status-hazard">HAZARD</span>}
                {u.source === "Crew" ? "Crew note" : (u.authorName ?? "Control Room")}
              </span>
              <span>{new Date(u.createdAtUtc).toLocaleString()}</span>
            </div>
            <p className="mt-1 text-body text-(--content-primary)">{u.text}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

function formatDistance(metres: number) {
  return metres >= 1000 ? `${(metres / 1000).toFixed(1)}km` : `${Math.round(metres)}m`;
}

function formatDuration(seconds: number) {
  const minutes = Math.round(seconds / 60);
  return minutes < 1 ? "under a minute" : `${minutes} min`;
}

// Which appliance to route from, plus every device at this station with a
// known position -- so the map shows "where are our own appliances" even
// before an operator has picked one to check a route against.
function RoutingPanel({ incident, onRouteChange }: {
  incident: IncidentDto;
  onRouteChange: (route: { appliances: DeviceDto[]; routePoints: [number, number][] | null }) => void;
}) {
  const [selectedDeviceId, setSelectedDeviceId] = useState<string>("");

  const devicesQuery = useQuery({ queryKey: ["devices"], queryFn: () => apiFetch<DeviceDto[]>("/devices") });
  const stationDevices = (devicesQuery.data ?? []).filter(
    (d) => d.orgUnitId === incident.orgUnitId && d.currentLatitude != null && d.currentLongitude != null,
  );

  useEffect(() => {
    if (!selectedDeviceId && stationDevices.length > 0) setSelectedDeviceId(stationDevices[0].id);
  }, [stationDevices.length]);

  const routeQuery = useQuery({
    queryKey: ["incident-route", incident.id, selectedDeviceId],
    queryFn: () => apiFetch<RouteResponseDto>(`/incidents/${incident.id}/route?deviceId=${selectedDeviceId}`),
    enabled: !!selectedDeviceId && incident.latitude != null && incident.longitude != null,
  });

  useEffect(() => {
    const points: [number, number][] | null = routeQuery.data?.available && routeQuery.data.points
      ? routeQuery.data.points.map((p) => [p.latitude, p.longitude])
      : null;
    onRouteChange({ appliances: stationDevices, routePoints: points });
  }, [routeQuery.data, stationDevices.length]);

  if (stationDevices.length === 0) {
    return (
      <p className="text-caption text-(--content-secondary)">
        No paired tablet at this station has reported a GPS position yet -- route unavailable.
      </p>
    );
  }

  return (
    <div className="flex flex-wrap items-center gap-3 text-caption text-(--content-secondary)">
      <label className="flex items-center gap-2">
        Route from
        <select
          value={selectedDeviceId}
          onChange={(e) => setSelectedDeviceId(e.target.value)}
          className="rounded-lg border border-(--surface-border) px-2 py-1"
        >
          {stationDevices.map((d) => <option key={d.id} value={d.id}>{d.label}</option>)}
        </select>
      </label>
      {routeQuery.data?.available && routeQuery.data.distanceMeters != null && routeQuery.data.durationSeconds != null && (
        <span className="font-semibold text-(--content-primary)">
          {formatDistance(routeQuery.data.distanceMeters)} &middot; {formatDuration(routeQuery.data.durationSeconds)}
        </span>
      )}
      {routeQuery.data && !routeQuery.data.available && <span>{routeQuery.data.unavailableReason}</span>}
    </div>
  );
}

export function IncidentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const incidentQuery = useQuery({
    queryKey: ["incident", id],
    queryFn: () => apiFetch<IncidentDto>(`/incidents/${id}`),
    refetchInterval: 20_000,
  });

  const statusMutation = useMutation({
    mutationFn: (status: "Open" | "Closed") =>
      apiFetch<IncidentDto>(`/incidents/${id}`, { method: "PATCH", body: JSON.stringify({ status }) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incident", id] });
      queryClient.invalidateQueries({ queryKey: ["incidents"] });
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const cancelMutation = useMutation({
    mutationFn: () => apiFetch<void>(`/incidents/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      showToast("Incident cancelled");
      queryClient.invalidateQueries({ queryKey: ["incidents"] });
      navigate("/");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const [route, setRoute] = useState<{ appliances: DeviceDto[]; routePoints: [number, number][] | null }>({
    appliances: [], routePoints: null,
  });

  if (incidentQuery.isLoading) return <p className="text-body text-(--content-secondary)">Loading...</p>;
  const incident = incidentQuery.data;
  if (!incident) return <p className="text-body text-(--content-secondary)">Incident not found.</p>;

  return (
    <div className="flex flex-col gap-4">
      <Link to="/" className="text-body text-brand-primary">&larr; Back to incidents</Link>

      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-page-title font-semibold text-(--content-primary)">{incident.incidentType}</h1>
          <p className="mt-1 text-body text-(--content-secondary)">
            {incident.address ?? "No address given"} &middot; {incident.orgUnitName}
          </p>
          {incident.description && <p className="mt-2 text-body text-(--content-primary)">{incident.description}</p>}
        </div>
        <div className="flex gap-2">
          {incident.status === "Open" && (
            <button
              type="button"
              onClick={() => statusMutation.mutate("Closed")}
              className="rounded-lg border border-(--surface-border) px-3 py-1.5 text-body text-(--content-primary)"
            >
              Close
            </button>
          )}
          {incident.status === "Closed" && (
            <button
              type="button"
              onClick={() => statusMutation.mutate("Open")}
              className="rounded-lg border border-(--surface-border) px-3 py-1.5 text-body text-(--content-primary)"
            >
              Reopen
            </button>
          )}
          {incident.status !== "Cancelled" && (
            <button
              type="button"
              onClick={() => {
                if (confirm("Cancel this incident? It will stay visible, marked as cancelled.")) cancelMutation.mutate();
              }}
              className="rounded-lg border border-status-hazard/40 px-3 py-1.5 text-body text-status-hazard"
            >
              Cancel incident
            </button>
          )}
        </div>
      </div>

      {incident.latitude != null && incident.longitude != null && (
        <div className="flex flex-col gap-2">
          <IncidentMap
            latitude={incident.latitude}
            longitude={incident.longitude}
            label={incident.address ?? incident.incidentType}
            appliances={route.appliances.map((d) => ({ label: d.label, latitude: d.currentLatitude!, longitude: d.currentLongitude! }))}
            routePoints={route.routePoints ?? undefined}
          />
          <RoutingPanel incident={incident} onRouteChange={setRoute} />
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <AttendancePanel incident={incident} />
        <TimelinePanel incident={incident} />
      </div>
    </div>
  );
}
