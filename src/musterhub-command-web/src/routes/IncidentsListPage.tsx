import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as Dialog from "@radix-ui/react-dialog";
import { apiFetch } from "../auth/apiClient";
import { useToast } from "../components/ToastProvider";
import { LocationPicker } from "../components/LocationPicker";
import { useIncidentHub } from "../hooks/useIncidentHub";
import type { CreateIncidentRequest, GeocodeResponseDto, IncidentDto, IncidentSummaryDto, MeResponse, OrgUnitDto } from "../api/types";

const STATUS_STYLES: Record<string, string> = {
  Open: "bg-status-open/15 text-status-open",
  Closed: "bg-status-closed/15 text-status-closed",
  Cancelled: "bg-status-cancelled/15 text-status-cancelled",
};

function StatusPill({ status }: { status: string }) {
  return (
    <span className={`rounded-full px-2 py-0.5 text-caption font-semibold ${STATUS_STYLES[status] ?? ""}`}>
      {status}
    </span>
  );
}

// "42m ago" while an incident's genuinely recent -- ticking every 30s is
// enough, this isn't a stopwatch. Falls back to an absolute date/time past
// a day so a week-old closed incident in the "All" view doesn't read as a
// meaningless "6d ago".
function ElapsedTime({ startedAtUtc }: { startedAtUtc: string }) {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 30_000);
    return () => clearInterval(id);
  }, []);

  const started = new Date(startedAtUtc).getTime();
  const minutes = Math.max(0, Math.round((now - started) / 60_000));

  if (minutes >= 24 * 60) return <>{new Date(startedAtUtc).toLocaleString()}</>;
  if (minutes < 60) return <>{minutes}m ago</>;
  return <>{Math.floor(minutes / 60)}h {minutes % 60}m ago</>;
}

function NewIncidentDialog({ stations }: { stations: OrgUnitDto[] }) {
  const [open, setOpen] = useState(false);
  const [latitude, setLatitude] = useState<number | null>(null);
  const [longitude, setLongitude] = useState<number | null>(null);
  const addressRef = useRef<HTMLInputElement>(null);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const locateMutation = useMutation({
    mutationFn: (query: string) => apiFetch<GeocodeResponseDto>(`/geocode?query=${encodeURIComponent(query)}`),
    onSuccess: (result) => {
      if (!result.found || result.latitude == null || result.longitude == null) {
        showToast("Couldn't find that address -- click the map to set it instead", "error");
        return;
      }
      setLatitude(result.latitude);
      setLongitude(result.longitude);
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const createMutation = useMutation({
    mutationFn: (request: CreateIncidentRequest) => apiFetch<IncidentDto>("/incidents", {
      method: "POST",
      body: JSON.stringify(request),
    }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incidents"] });
      showToast("Incident created");
      setOpen(false);
      setLatitude(null);
      setLongitude(null);
    },
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger asChild>
        <button type="button" className="rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white">
          New incident
        </button>
      </Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 bg-black/40" />
        <Dialog.Content className="fixed left-1/2 top-1/2 w-full max-w-md -translate-x-1/2 -translate-y-1/2 rounded-card bg-(--surface) p-4 shadow-card">
          <Dialog.Title className="text-card-title font-semibold text-(--content-primary)">
            New incident
          </Dialog.Title>
          <form
            className="mt-4 flex flex-col gap-3"
            onSubmit={(e) => {
              e.preventDefault();
              const form = new FormData(e.currentTarget);
              createMutation.mutate({
                externalReference: `MANUAL-${crypto.randomUUID().slice(0, 8).toUpperCase()}`,
                incidentType: String(form.get("incidentType")),
                description: String(form.get("description") || "") || null,
                address: String(form.get("address") || "") || null,
                stationCode: String(form.get("stationCode")),
                latitude,
                longitude,
              });
            }}
          >
            <label className="flex flex-col gap-1 text-body text-(--content-primary)">
              Incident type
              <input name="incidentType" required className="rounded-lg border border-(--surface-border) px-3 py-2" placeholder="RTC, Structure Fire, ..." />
            </label>
            <label className="flex flex-col gap-1 text-body text-(--content-primary)">
              Station
              <select name="stationCode" required className="rounded-lg border border-(--surface-border) px-3 py-2">
                <option value="">Select a station</option>
                {stations.map((s) => (
                  <option key={s.id} value={s.code ?? ""}>{s.name}</option>
                ))}
              </select>
            </label>
            <label className="flex flex-col gap-1 text-body text-(--content-primary)">
              Address
              <div className="flex gap-2">
                <input ref={addressRef} name="address" className="flex-1 rounded-lg border border-(--surface-border) px-3 py-2" />
                <button
                  type="button"
                  disabled={locateMutation.isPending}
                  onClick={() => {
                    const query = addressRef.current?.value.trim();
                    if (query) locateMutation.mutate(query);
                  }}
                  className="rounded-lg border border-(--surface-border) px-3 py-2 text-body text-(--content-primary) disabled:opacity-60"
                >
                  {locateMutation.isPending ? "Locating..." : "Locate"}
                </button>
              </div>
            </label>
            <label className="flex flex-col gap-1 text-body text-(--content-primary)">
              Description
              <textarea name="description" className="rounded-lg border border-(--surface-border) px-3 py-2" rows={2} />
            </label>
            <div className="flex flex-col gap-1 text-body text-(--content-primary)">
              Location (optional -- Vision-fed incidents already carry one)
              <LocationPicker latitude={latitude} longitude={longitude} onChange={(lat, lng) => { setLatitude(lat); setLongitude(lng); }} />
            </div>
            <div className="mt-2 flex justify-end gap-2">
              <Dialog.Close asChild>
                <button type="button" className="rounded-lg px-4 py-2 text-body text-(--content-secondary)">Cancel</button>
              </Dialog.Close>
              <button
                type="submit"
                disabled={createMutation.isPending}
                className="rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white disabled:opacity-60"
              >
                {createMutation.isPending ? "Creating..." : "Create"}
              </button>
            </div>
          </form>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export function IncidentsListPage() {
  const [activeOnly, setActiveOnly] = useState(true);
  const [search, setSearch] = useState("");
  const [stationFilter, setStationFilter] = useState("");

  const meQuery = useQuery({ queryKey: ["me"], queryFn: () => apiFetch<MeResponse>("/me") });
  // 60s here is a fallback, not the primary path -- useIncidentHub below
  // pushes a refetch the moment any incident in the org actually changes.
  const incidentsQuery = useQuery({
    queryKey: ["incidents", activeOnly],
    queryFn: () => apiFetch<IncidentSummaryDto[]>(`/incidents?activeOnly=${activeOnly}`),
    refetchInterval: 60_000,
  });
  const liveConnected = useIncidentHub(meQuery.data?.organisationId);
  const stationsQuery = useQuery({
    queryKey: ["org-units"],
    queryFn: () => apiFetch<OrgUnitDto[]>("/org-units"),
  });
  const stations = (stationsQuery.data ?? []).filter((u) => u.orgUnitTypeName === "Station");

  // Client-side -- the list isn't large enough yet to warrant a server-side
  // search endpoint.
  const normalizedSearch = search.trim().toLowerCase();
  const visibleIncidents = (incidentsQuery.data ?? [])
    .filter((i) => stationFilter === "" || i.orgUnitId === stationFilter)
    .filter((i) => normalizedSearch === "" || [i.incidentType, i.address, i.orgUnitName]
      .some((field) => field?.toLowerCase().includes(normalizedSearch)));

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h1 className="text-page-title font-semibold text-(--content-primary)">Incidents</h1>
          <span
            title={liveConnected ? "Live" : "Reconnecting..."}
            className={`h-2 w-2 rounded-full ${liveConnected ? "bg-status-on-scene" : "bg-(--content-secondary)"}`}
          />
        </div>
        <NewIncidentDialog stations={stations} />
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <button
          type="button"
          onClick={() => setActiveOnly(true)}
          className={`rounded-lg px-3 py-1.5 text-body ${activeOnly ? "bg-brand-primary text-white" : "bg-(--surface) text-(--content-secondary)"}`}
        >
          Active
        </button>
        <button
          type="button"
          onClick={() => setActiveOnly(false)}
          className={`rounded-lg px-3 py-1.5 text-body ${!activeOnly ? "bg-brand-primary text-white" : "bg-(--surface) text-(--content-secondary)"}`}
        >
          All
        </button>
        <select
          value={stationFilter}
          onChange={(e) => setStationFilter(e.target.value)}
          aria-label="Filter by station"
          className="rounded-lg border border-(--surface-border) px-3 py-1.5 text-body text-(--content-primary)"
        >
          <option value="">All stations</option>
          {stations.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
        </select>
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search type, address, station..."
          aria-label="Search incidents"
          className="flex-1 min-w-48 rounded-lg border border-(--surface-border) px-3 py-1.5 text-body text-(--content-primary)"
        />
      </div>

      {incidentsQuery.isLoading && <p className="text-body text-(--content-secondary)">Loading...</p>}
      {incidentsQuery.isError && (
        <div className="flex items-center justify-between rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card">
          <p className="text-body text-(--content-secondary)">Couldn't load incidents. Check your connection.</p>
          <button
            type="button"
            onClick={() => incidentsQuery.refetch()}
            className="rounded-lg border border-(--surface-border) px-3 py-1.5 text-body font-semibold text-(--content-primary)"
          >
            Try again
          </button>
        </div>
      )}
      {incidentsQuery.data?.length === 0 && (
        <p className="text-body text-(--content-secondary)">
          {activeOnly ? "No active incidents." : "No incidents yet."}
        </p>
      )}
      {incidentsQuery.data && incidentsQuery.data.length > 0 && visibleIncidents.length === 0 && (
        <p className="text-body text-(--content-secondary)">Nothing matches that search/filter.</p>
      )}

      <div className="flex flex-col gap-2">
        {visibleIncidents.map((incident) => (
          <Link
            key={incident.id}
            to={`/incidents/${incident.id}`}
            className="flex items-center justify-between rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card hover:border-brand-primary"
          >
            <div>
              <div className="flex items-center gap-2">
                <span className="text-card-title font-semibold text-(--content-primary)">{incident.incidentType}</span>
                <StatusPill status={incident.status} />
              </div>
              <p className="mt-1 text-body text-(--content-secondary)">
                {incident.address ?? "No address given"} &middot; {incident.orgUnitName}
              </p>
            </div>
            <span className="text-caption text-(--content-secondary)">
              <ElapsedTime startedAtUtc={incident.startedAtUtc} />
            </span>
          </Link>
        ))}
      </div>
    </div>
  );
}
