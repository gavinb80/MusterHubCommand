import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "../auth/apiClient";
import { useToast } from "../components/ToastProvider";
import type {
  CreateDeviceResponse, CreateIntegrationApiKeyResponse, DeviceDto, EmployeeDto,
  IntegrationApiKeyDto, OrgUnitDto,
} from "../api/types";

const TABS = ["Stations", "Devices", "Integration keys", "Operators"] as const;
type Tab = (typeof TABS)[number];

function RevealOnceBanner({ label, secret, onDismiss }: { label: string; secret: string; onDismiss: () => void }) {
  return (
    <div className="rounded-lg border border-status-open/40 bg-status-open/10 p-3">
      <p className="text-body font-semibold text-(--content-primary)">{label} -- copy this now, it won't be shown again</p>
      <code className="mt-1 block break-all rounded bg-(--surface-alt) p-2 text-caption text-(--content-primary)">{secret}</code>
      <button type="button" onClick={onDismiss} className="mt-2 text-caption text-brand-primary">Done</button>
    </div>
  );
}

function StationsTab() {
  const stationsQuery = useQuery({ queryKey: ["org-units"], queryFn: () => apiFetch<OrgUnitDto[]>("/org-units") });
  const stations = (stationsQuery.data ?? []).filter((u) => u.orgUnitTypeName === "Station");

  return (
    <div className="flex flex-col gap-2">
      <p className="text-body text-(--content-secondary)">
        Mirrored from MusterHub's own directory -- synced nightly, or on demand from the core directory sync job.
        Vision pushes incidents against a station's code below.
      </p>
      {stations.map((s) => (
        <div key={s.id} className="flex items-center justify-between rounded-card border border-(--surface-border) bg-(--surface) p-3">
          <span className="text-body font-medium text-(--content-primary)">{s.name}</span>
          <code className="text-caption text-(--content-secondary)">{s.code ?? "(no code)"}</code>
        </div>
      ))}
    </div>
  );
}

function DevicesTab() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const [revealed, setRevealed] = useState<CreateDeviceResponse | null>(null);

  const devicesQuery = useQuery({ queryKey: ["devices"], queryFn: () => apiFetch<DeviceDto[]>("/devices") });
  const stationsQuery = useQuery({ queryKey: ["org-units"], queryFn: () => apiFetch<OrgUnitDto[]>("/org-units") });
  const stations = (stationsQuery.data ?? []).filter((u) => u.orgUnitTypeName === "Station");

  const createMutation = useMutation({
    mutationFn: (body: { label: string; orgUnitId: string }) =>
      apiFetch<CreateDeviceResponse>("/devices", { method: "POST", body: JSON.stringify(body) }),
    onSuccess: (device) => {
      queryClient.invalidateQueries({ queryKey: ["devices"] });
      setRevealed(device);
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const revokeMutation = useMutation({
    mutationFn: (id: string) => apiFetch<void>(`/devices/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["devices"] });
      showToast("Device revoked");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <div className="flex flex-col gap-3">
      <p className="text-body text-(--content-secondary)">
        Pair an appliance tablet by registering it here, then enter the pairing code shown once into the
        tablet's own first-run screen.
      </p>

      {revealed && (
        <RevealOnceBanner label={`Pairing code for "${revealed.label}"`} secret={revealed.pairingToken} onDismiss={() => setRevealed(null)} />
      )}

      <form
        className="flex items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          const form = new FormData(e.currentTarget);
          createMutation.mutate({ label: String(form.get("label")), orgUnitId: String(form.get("orgUnitId")) });
          e.currentTarget.reset();
        }}
      >
        <label className="flex flex-col gap-1 text-body text-(--content-primary)">
          Label
          <input name="label" required placeholder="Engine 1 tablet" className="rounded-lg border border-(--surface-border) px-3 py-2" />
        </label>
        <label className="flex flex-col gap-1 text-body text-(--content-primary)">
          Station
          <select name="orgUnitId" required className="rounded-lg border border-(--surface-border) px-3 py-2">
            <option value="">Select a station</option>
            {stations.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </label>
        <button type="submit" disabled={createMutation.isPending} className="rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white disabled:opacity-60">
          Pair device
        </button>
      </form>

      <div className="flex flex-col gap-2">
        {devicesQuery.data?.map((d) => (
          <div key={d.id} className="flex items-center justify-between rounded-card border border-(--surface-border) bg-(--surface) p-3">
            <div>
              <p className="text-body font-medium text-(--content-primary)">{d.label}</p>
              <p className="text-caption text-(--content-secondary)">
                {d.orgUnitName} &middot; {d.isActive ? "Active" : "Revoked"}
                {d.lastSeenAtUtc && ` · last seen ${new Date(d.lastSeenAtUtc).toLocaleString()}`}
              </p>
            </div>
            {d.isActive && (
              <button
                type="button"
                onClick={() => { if (confirm(`Revoke "${d.label}"? It will be signed out immediately.`)) revokeMutation.mutate(d.id); }}
                className="text-body text-status-hazard"
              >
                Revoke
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function IntegrationKeysTab() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const [revealed, setRevealed] = useState<CreateIntegrationApiKeyResponse | null>(null);

  const keysQuery = useQuery({ queryKey: ["integration-api-keys"], queryFn: () => apiFetch<IntegrationApiKeyDto[]>("/integration-api-keys") });

  const createMutation = useMutation({
    mutationFn: (label: string) => apiFetch<CreateIntegrationApiKeyResponse>("/integration-api-keys", { method: "POST", body: JSON.stringify({ label }) }),
    onSuccess: (key) => {
      queryClient.invalidateQueries({ queryKey: ["integration-api-keys"] });
      setRevealed(key);
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const revokeMutation = useMutation({
    mutationFn: (id: string) => apiFetch<void>(`/integration-api-keys/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["integration-api-keys"] });
      showToast("Key revoked");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <div className="flex flex-col gap-3">
      <p className="text-body text-(--content-secondary)">
        Issue a key for Vision (or any equivalent control-room system) to push incidents against
        <code className="mx-1 rounded bg-(--surface-alt) px-1">POST /api/integrations/incidents</code>
        with an <code className="rounded bg-(--surface-alt) px-1">X-Api-Key</code> header.
      </p>

      {revealed && (
        <RevealOnceBanner label={`API key "${revealed.label}"`} secret={revealed.apiKey} onDismiss={() => setRevealed(null)} />
      )}

      <form
        className="flex items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          const form = new FormData(e.currentTarget);
          createMutation.mutate(String(form.get("label")));
          e.currentTarget.reset();
        }}
      >
        <label className="flex flex-col gap-1 text-body text-(--content-primary)">
          Label
          <input name="label" required placeholder="Vision" className="rounded-lg border border-(--surface-border) px-3 py-2" />
        </label>
        <button type="submit" disabled={createMutation.isPending} className="rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white disabled:opacity-60">
          Issue key
        </button>
      </form>

      <div className="flex flex-col gap-2">
        {keysQuery.data?.map((k) => (
          <div key={k.id} className="flex items-center justify-between rounded-card border border-(--surface-border) bg-(--surface) p-3">
            <div>
              <p className="text-body font-medium text-(--content-primary)">{k.label}</p>
              <p className="text-caption text-(--content-secondary)">
                {k.isActive ? "Active" : "Revoked"}
                {k.lastUsedAtUtc && ` · last used ${new Date(k.lastUsedAtUtc).toLocaleString()}`}
              </p>
            </div>
            {k.isActive && (
              <button
                type="button"
                onClick={() => { if (confirm(`Revoke "${k.label}"?`)) revokeMutation.mutate(k.id); }}
                className="text-body text-status-hazard"
              >
                Revoke
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function OperatorsTab() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const employeesQuery = useQuery({ queryKey: ["employees"], queryFn: () => apiFetch<EmployeeDto[]>("/employees") });

  const setOperatorMutation = useMutation({
    mutationFn: ({ id, isOperator }: { id: string; isOperator: boolean }) =>
      apiFetch<void>(`/employees/${id}/operator`, { method: "PUT", body: JSON.stringify(isOperator) }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["employees"] }),
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <div className="flex flex-col gap-2">
      <p className="text-body text-(--content-secondary)">
        Control Room Operators can create/edit incidents, push updates, and manage devices and integration keys.
      </p>
      {employeesQuery.data?.map((e) => (
        <div key={e.id} className="flex items-center justify-between rounded-card border border-(--surface-border) bg-(--surface) p-3">
          <span className="text-body text-(--content-primary)">{e.displayName}</span>
          <label className="flex items-center gap-2 text-body text-(--content-secondary)">
            Operator
            <input
              type="checkbox"
              checked={e.isOperator}
              onChange={(ev) => setOperatorMutation.mutate({ id: e.id, isOperator: ev.target.checked })}
            />
          </label>
        </div>
      ))}
    </div>
  );
}

export function SetupPage() {
  const [tab, setTab] = useState<Tab>("Stations");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-page-title font-semibold text-(--content-primary)">Setup</h1>
      <div className="flex gap-2 border-b border-(--surface-border)">
        {TABS.map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setTab(t)}
            className={`px-3 py-2 text-body ${tab === t ? "border-b-2 border-brand-primary font-semibold text-(--content-primary)" : "text-(--content-secondary)"}`}
          >
            {t}
          </button>
        ))}
      </div>
      {tab === "Stations" && <StationsTab />}
      {tab === "Devices" && <DevicesTab />}
      {tab === "Integration keys" && <IntegrationKeysTab />}
      {tab === "Operators" && <OperatorsTab />}
    </div>
  );
}
