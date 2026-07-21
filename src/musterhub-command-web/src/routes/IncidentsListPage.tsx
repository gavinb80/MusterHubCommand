import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as Dialog from "@radix-ui/react-dialog";
import { apiFetch } from "../auth/apiClient";
import { useToast } from "../components/ToastProvider";
import type { CreateIncidentRequest, IncidentDto, IncidentSummaryDto, OrgUnitDto } from "../api/types";

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

function NewIncidentDialog({ stations }: { stations: OrgUnitDto[] }) {
  const [open, setOpen] = useState(false);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const createMutation = useMutation({
    mutationFn: (request: CreateIncidentRequest) => apiFetch<IncidentDto>("/incidents", {
      method: "POST",
      body: JSON.stringify(request),
    }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incidents"] });
      showToast("Incident created");
      setOpen(false);
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
              <input name="address" className="rounded-lg border border-(--surface-border) px-3 py-2" />
            </label>
            <label className="flex flex-col gap-1 text-body text-(--content-primary)">
              Description
              <textarea name="description" className="rounded-lg border border-(--surface-border) px-3 py-2" rows={2} />
            </label>
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

  const incidentsQuery = useQuery({
    queryKey: ["incidents", activeOnly],
    queryFn: () => apiFetch<IncidentSummaryDto[]>(`/incidents?activeOnly=${activeOnly}`),
    refetchInterval: 20_000,
  });
  const stationsQuery = useQuery({
    queryKey: ["org-units"],
    queryFn: () => apiFetch<OrgUnitDto[]>("/org-units"),
  });
  const stations = (stationsQuery.data ?? []).filter((u) => u.orgUnitTypeName === "Station");

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-page-title font-semibold text-(--content-primary)">Incidents</h1>
        <NewIncidentDialog stations={stations} />
      </div>

      <div className="flex gap-2">
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
      </div>

      {incidentsQuery.isLoading && <p className="text-body text-(--content-secondary)">Loading...</p>}
      {incidentsQuery.data?.length === 0 && (
        <p className="text-body text-(--content-secondary)">
          {activeOnly ? "No active incidents." : "No incidents yet."}
        </p>
      )}

      <div className="flex flex-col gap-2">
        {incidentsQuery.data?.map((incident) => (
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
              {new Date(incident.startedAtUtc).toLocaleString()}
            </span>
          </Link>
        ))}
      </div>
    </div>
  );
}
