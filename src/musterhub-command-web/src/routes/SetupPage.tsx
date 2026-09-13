import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "../auth/apiClient";
import { useToast } from "../components/ToastProvider";
import type {
  CommandOperatorTier, CoreDirectoryImportSummary, CreateDeviceResponse, CreateIntegrationApiKeyResponse, DeviceDto,
  EmployeeDto, IncidentCloseTypeDto, IntegrationApiKeyDto, MeResponse, OrganisationSettingsDto, OrgUnitDto,
  SaveVehicleProfileRequest, SyncConfigDto, VehicleProfileDto,
} from "../api/types";

const TABS = ["Stations", "Devices", "Vehicle profiles", "Close types", "Integration keys", "Operators", "General"] as const;
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
      {stationsQuery.isSuccess && stations.length === 0 && (
        <p className="text-body text-(--content-secondary)">
          Nothing here yet. Run the import under General &rarr; Import from MusterHub to pull your stations in.
        </p>
      )}
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
  const profilesQuery = useQuery({ queryKey: ["vehicle-profiles"], queryFn: () => apiFetch<VehicleProfileDto[]>("/vehicle-profiles") });

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

  const setProfileMutation = useMutation({
    mutationFn: ({ id, vehicleProfileId }: { id: string; vehicleProfileId: string | null }) =>
      apiFetch<DeviceDto>(`/devices/${id}/vehicle-profile`, { method: "PUT", body: JSON.stringify(vehicleProfileId) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["devices"] });
      showToast("Vehicle profile updated");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const setCallsignMutation = useMutation({
    mutationFn: ({ id, callsign }: { id: string; callsign: string | null }) =>
      apiFetch<DeviceDto>(`/devices/${id}/callsign`, { method: "PUT", body: JSON.stringify(callsign) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["devices"] });
      showToast("Callsign updated");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <div className="flex flex-col gap-3">
      <p className="text-body text-(--content-secondary)">
        Pair an appliance tablet by registering it here, then enter the pairing code shown once into the
        tablet's own first-run screen. Assign a vehicle profile so routes on the incident map respect this
        appliance's real dimensions.
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
                {d.locationUpdatedAtUtc && ` · GPS updated ${new Date(d.locationUpdatedAtUtc).toLocaleString()}`}
              </p>
            </div>
            <div className="flex items-center gap-3">
              <label className="flex items-center gap-2 text-caption text-(--content-secondary)">
                Callsign
                <input
                  key={d.callsign ?? ""}
                  defaultValue={d.callsign ?? ""}
                  placeholder="KV57P1"
                  onBlur={(e) => {
                    const value = e.target.value.trim() || null;
                    if (value !== d.callsign) setCallsignMutation.mutate({ id: d.id, callsign: value });
                  }}
                  className="w-24 rounded-lg border border-(--surface-border) px-2 py-1"
                  title="Which attendance entry this tablet's own actions (e.g. Start navigation) update on the incident"
                />
              </label>
              <label className="flex items-center gap-2 text-caption text-(--content-secondary)">
                Vehicle profile
                <select
                  value={d.vehicleProfileId ?? ""}
                  onChange={(e) => setProfileMutation.mutate({ id: d.id, vehicleProfileId: e.target.value || null })}
                  className="rounded-lg border border-(--surface-border) px-2 py-1"
                >
                  <option value="">Unrestricted</option>
                  {profilesQuery.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
              </label>
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
          </div>
        ))}
      </div>
    </div>
  );
}

function VehicleProfilesTab() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const profilesQuery = useQuery({ queryKey: ["vehicle-profiles"], queryFn: () => apiFetch<VehicleProfileDto[]>("/vehicle-profiles") });

  const createMutation = useMutation({
    mutationFn: (body: SaveVehicleProfileRequest) => apiFetch<VehicleProfileDto>("/vehicle-profiles", { method: "POST", body: JSON.stringify(body) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["vehicle-profiles"] });
      showToast("Vehicle profile added");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => apiFetch<void>(`/vehicle-profiles/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["vehicle-profiles"] });
      showToast("Vehicle profile deleted");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const parseOptionalNumber = (value: FormDataEntryValue | null) => {
    const trimmed = String(value ?? "").trim();
    return trimmed === "" ? null : Number(trimmed);
  };

  return (
    <div className="flex flex-col gap-3">
      <p className="text-body text-(--content-secondary)">
        An appliance class's real physical limits. Routes computed for a device assigned one of these
        genuinely avoid a road tagged below what the appliance can fit or carry -- not just a slower
        speed assumption. Leave a field blank for "no known restriction."
      </p>

      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          const form = new FormData(e.currentTarget);
          createMutation.mutate({
            name: String(form.get("name")),
            maxWeightTonnes: parseOptionalNumber(form.get("maxWeightTonnes")),
            maxHeightMetres: parseOptionalNumber(form.get("maxHeightMetres")),
            maxWidthMetres: parseOptionalNumber(form.get("maxWidthMetres")),
          });
          e.currentTarget.reset();
        }}
      >
        <label className="flex flex-col gap-1 text-body text-(--content-primary)">
          Name
          <input name="name" required placeholder="Aerial Ladder Platform" className="w-48 rounded-lg border border-(--surface-border) px-3 py-2" />
        </label>
        <label className="flex flex-col gap-1 text-body text-(--content-primary)">
          Max weight (t)
          <input name="maxWeightTonnes" type="number" step="0.1" min="0" className="w-28 rounded-lg border border-(--surface-border) px-3 py-2" />
        </label>
        <label className="flex flex-col gap-1 text-body text-(--content-primary)">
          Max height (m)
          <input name="maxHeightMetres" type="number" step="0.1" min="0" className="w-28 rounded-lg border border-(--surface-border) px-3 py-2" />
        </label>
        <label className="flex flex-col gap-1 text-body text-(--content-primary)">
          Max width (m)
          <input name="maxWidthMetres" type="number" step="0.1" min="0" className="w-28 rounded-lg border border-(--surface-border) px-3 py-2" />
        </label>
        <button type="submit" disabled={createMutation.isPending} className="rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white disabled:opacity-60">
          Add profile
        </button>
      </form>

      <div className="flex flex-col gap-2">
        {profilesQuery.data?.length === 0 && (
          <p className="text-body text-(--content-secondary)">No vehicle profiles yet -- devices route as an unrestricted vehicle.</p>
        )}
        {profilesQuery.data?.map((p) => (
          <div key={p.id} className="flex items-center justify-between rounded-card border border-(--surface-border) bg-(--surface) p-3">
            <div>
              <p className="text-body font-medium text-(--content-primary)">{p.name}</p>
              <p className="text-caption text-(--content-secondary)">
                {[
                  p.maxWeightTonnes != null ? `max ${p.maxWeightTonnes}t` : null,
                  p.maxHeightMetres != null ? `max ${p.maxHeightMetres}m tall` : null,
                  p.maxWidthMetres != null ? `max ${p.maxWidthMetres}m wide` : null,
                ].filter(Boolean).join(" · ") || "No restrictions set"}
              </p>
            </div>
            <button
              type="button"
              onClick={() => { if (confirm(`Delete "${p.name}"? Devices using it fall back to unrestricted routing.`)) deleteMutation.mutate(p.id); }}
              className="text-body text-status-hazard"
            >
              Delete
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}

function CloseTypesTab() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const closeTypesQuery = useQuery({ queryKey: ["incident-close-types"], queryFn: () => apiFetch<IncidentCloseTypeDto[]>("/incident-close-types") });

  const createMutation = useMutation({
    mutationFn: (body: { code: string; name: string }) => apiFetch<IncidentCloseTypeDto>("/incident-close-types", { method: "POST", body: JSON.stringify(body) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incident-close-types"] });
      showToast("Close type added");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => apiFetch<void>(`/incident-close-types/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incident-close-types"] });
      showToast("Close type deleted");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <div className="flex flex-col gap-3">
      <p className="text-body text-(--content-secondary)">
        The classification codes offered when closing an incident, e.g. "M1.2.3 - Fire In Open".
        A code already used to close an incident can't be deleted -- that would rewrite what the
        incident was actually closed against.
      </p>

      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          const form = new FormData(e.currentTarget);
          createMutation.mutate({ code: String(form.get("code")), name: String(form.get("name")) });
          e.currentTarget.reset();
        }}
      >
        <label className="flex flex-col gap-1 text-body text-(--content-primary)">
          Code
          <input name="code" required placeholder="M1.2.3" className="w-32 rounded-lg border border-(--surface-border) px-3 py-2" />
        </label>
        <label className="flex flex-col gap-1 text-body text-(--content-primary)">
          Name
          <input name="name" required placeholder="Fire In Open" className="w-64 rounded-lg border border-(--surface-border) px-3 py-2" />
        </label>
        <button type="submit" disabled={createMutation.isPending} className="rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white disabled:opacity-60">
          Add close type
        </button>
      </form>

      <div className="flex flex-col gap-2">
        {closeTypesQuery.data?.length === 0 && (
          <p className="text-body text-(--content-secondary)">No close types yet -- closing an incident won't offer a classification.</p>
        )}
        {closeTypesQuery.data?.map((t) => (
          <div key={t.id} className="flex items-center justify-between rounded-card border border-(--surface-border) bg-(--surface) p-3">
            <p className="text-body text-(--content-primary)"><span className="font-semibold">{t.code}</span>: {t.name}</p>
            <button
              type="button"
              onClick={() => { if (confirm(`Delete "${t.code} - ${t.name}"?`)) deleteMutation.mutate(t.id); }}
              className="text-body text-status-hazard"
            >
              Delete
            </button>
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
        Issue a key for any control-room or automation system to push incidents against
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
  // Same cache key every other page's account block/gating already
  // queries -- shares that result rather than firing a second /me request.
  const meQuery = useQuery({ queryKey: ["me"], queryFn: () => apiFetch<MeResponse>("/me") });
  const canManage = meQuery.data?.isIncidentCommander ?? false;

  const setOperatorMutation = useMutation({
    mutationFn: ({ id, tier }: { id: string; tier: CommandOperatorTier | null }) =>
      apiFetch<void>(`/employees/${id}/operator`, { method: "PUT", body: JSON.stringify({ tier }) }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["employees"] }),
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <div className="flex flex-col gap-2">
      <p className="text-body text-(--content-secondary)">
        Control Room and Command Support both create/edit incidents, push updates, and manage devices and
        integration keys. Incident Commander adds closing/cancelling incidents, managing sectors and hierarchy, and
        granting/revoking operators.
        {!canManage && " Only an Incident Commander can change these."}
      </p>
      {employeesQuery.isSuccess && employeesQuery.data.length === 0 && (
        <p className="text-body text-(--content-secondary)">
          No one to assign yet. Run the import under General &rarr; Import from MusterHub to pull your people in.
        </p>
      )}
      {employeesQuery.data?.map((e) => (
        <div key={e.id} className="flex items-center justify-between rounded-card border border-(--surface-border) bg-(--surface) p-3">
          <span className="text-body text-(--content-primary)">{e.displayName}</span>
          <label className="flex items-center gap-2 text-body text-(--content-secondary)">
            Operator tier
            <select
              disabled={!canManage}
              value={e.operatorTier ?? ""}
              onChange={(ev) => setOperatorMutation.mutate({ id: e.id, tier: (ev.target.value || null) as CommandOperatorTier | null })}
              className="rounded-lg border border-(--surface-border) px-2 py-1 text-body text-(--content-primary) disabled:opacity-60"
            >
              <option value="">Not an operator</option>
              <option value="ControlRoom">Control Room</option>
              <option value="CommandSupport">Command Support</option>
              <option value="IncidentCommander">Incident Commander</option>
            </select>
          </label>
        </div>
      ))}
    </div>
  );
}

function DirectorySyncSection() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const [apiKey, setApiKey] = useState("");
  const [summary, setSummary] = useState<CoreDirectoryImportSummary | null>(null);

  const syncConfigQuery = useQuery({
    queryKey: ["core-sync-config"],
    queryFn: () => apiFetch<SyncConfigDto>("/import/sync-config"),
  });

  const importMutation = useMutation({
    mutationFn: () => apiFetch<CoreDirectoryImportSummary>("/import/core-directory", { method: "POST", body: JSON.stringify({ apiKey }) }),
    onSuccess: (result) => {
      setSummary(result);
      setApiKey("");
      queryClient.invalidateQueries();
    },
    onError: (error) => showToast(error.message, "error"),
  });

  // Saving the key runs an import right now too -- a bad key never sticks --
  // then the nightly job takes over from here on.
  const enableSyncMutation = useMutation({
    mutationFn: () => apiFetch<CoreDirectoryImportSummary>("/import/sync-config", { method: "PUT", body: JSON.stringify({ apiKey }) }),
    onSuccess: (result) => {
      setSummary(result);
      setApiKey("");
      queryClient.invalidateQueries();
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const disableSyncMutation = useMutation({
    mutationFn: () => apiFetch<void>("/import/sync-config", { method: "DELETE" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["core-sync-config"] }),
    onError: (error) => showToast(error.message, "error"),
  });

  const sync = syncConfigQuery.data;
  const busy = importMutation.isPending || enableSyncMutation.isPending;

  return (
    <div className="flex flex-col gap-2 rounded-card border border-(--surface-border) bg-(--surface) p-3">
      <p className="text-body font-semibold text-(--content-primary)">Import from MusterHub</p>
      <p className="text-caption text-(--content-secondary)">
        Pulls your service's stations and people straight from MusterHub, so nothing here needs retyping. Create
        an API key with the Directory Export permission in MusterHub's admin portal (Integrations), paste it
        below, and either run a one-off import or save it so the sync runs every night on its own.
      </p>
      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(ev) => {
          ev.preventDefault();
          importMutation.mutate();
        }}
      >
        <input
          type="password"
          placeholder="MusterHub API key"
          value={apiKey}
          onChange={(ev) => setApiKey(ev.target.value)}
          required
          className="w-80 max-w-full rounded-lg border border-(--surface-border) px-3 py-2 text-body"
        />
        <button
          type="submit"
          disabled={busy}
          className="rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white disabled:opacity-60"
        >
          {importMutation.isPending ? "Importing..." : "Run import"}
        </button>
        <button
          type="button"
          disabled={busy || !apiKey}
          onClick={() => enableSyncMutation.mutate()}
          className="rounded-lg border border-(--surface-border) px-4 py-2 text-body text-(--content-primary) disabled:opacity-60"
        >
          {enableSyncMutation.isPending ? "Saving..." : sync?.enabled ? "Update nightly sync key" : "Save and sync nightly"}
        </button>
      </form>
      {sync?.enabled && (
        <p className="text-caption text-(--content-primary)">
          Nightly sync is on.
          {sync.lastSyncedAtUtc && ` Last synced ${sync.lastSyncedAtUtc.slice(0, 10)} ${sync.lastSyncedAtUtc.slice(11, 16)}`}
          {sync.lastSyncNote && ` (${sync.lastSyncNote})`}.{" "}
          <button type="button" className="text-(--content-secondary) underline" onClick={() => disableSyncMutation.mutate()}>
            Turn off and forget the key
          </button>
        </p>
      )}
      {summary && (
        <p className="text-caption text-(--content-primary)">
          Imported from <span className="font-semibold">{summary.serviceName}</span>: {summary.unitsCreated} stations
          created, {summary.unitsUpdated} updated &middot; {summary.employeesCreated} people created,{" "}
          {summary.employeesUpdated} updated &middot; {summary.stationMembershipsChanged} station links changed.
        </p>
      )}
    </div>
  );
}

function GeneralTab() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const settingsQuery = useQuery({
    queryKey: ["organisation-settings"],
    queryFn: () => apiFetch<OrganisationSettingsDto>("/organisation-settings"),
  });

  // Always sends both fields -- OrganisationSettings is one row, and a PUT
  // that only carried the field a particular form happened to touch would
  // silently reset every other field to its JSON default (BaEntryControlEnabled
  // back to false on every geofence save, for instance).
  const saveMutation = useMutation({
    mutationFn: (settings: { geofenceRadiusMeters: number; baEntryControlEnabled: boolean }) =>
      apiFetch<OrganisationSettingsDto>("/organisation-settings", {
        method: "PUT",
        body: JSON.stringify(settings),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["organisation-settings"] });
      showToast("Settings updated");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  if (settingsQuery.isLoading) return <p className="text-body text-(--content-secondary)">Loading...</p>;

  return (
    <div className="flex flex-col gap-3">
      <DirectorySyncSection />
      <p className="text-body text-(--content-secondary)">Org-wide defaults for the tablet app.</p>
      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          const form = new FormData(e.currentTarget);
          const value = Number(form.get("geofenceRadiusMeters"));
          if (value > 0) {
            saveMutation.mutate({ geofenceRadiusMeters: value, baEntryControlEnabled: form.get("baEntryControlEnabled") === "on" });
          }
        }}
      >
        <label className="flex flex-col gap-1 text-body text-(--content-primary)">
          Arrival geofence radius (metres)
          <input
            key={settingsQuery.data?.geofenceRadiusMeters}
            name="geofenceRadiusMeters"
            type="number"
            min="1"
            step="1"
            defaultValue={settingsQuery.data?.geofenceRadiusMeters}
            className="w-32 rounded-lg border border-(--surface-border) px-3 py-2"
          />
        </label>
        <label className="flex items-center gap-2 text-body text-(--content-primary)">
          <input
            key={String(settingsQuery.data?.baEntryControlEnabled)}
            name="baEntryControlEnabled"
            type="checkbox"
            defaultChecked={settingsQuery.data?.baEntryControlEnabled ?? false}
          />
          BA Entry Control
        </label>
        <button
          type="submit"
          disabled={saveMutation.isPending}
          className="self-start rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white disabled:opacity-60"
        >
          Save
        </button>
      </form>
      <p className="text-caption text-(--content-secondary)">
        When a paired tablet's reported GPS falls within this distance of an incident's location, that appliance
        is automatically marked OnScene -- no action needed from the crew.
      </p>
      <p className="text-caption text-(--content-secondary)">
        BA Entry Control adds a Risk Log-adjacent tab to every incident for tracking breathing apparatus wearers
        in and out. Off by default -- turn it on if this service runs BA boards.
      </p>
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
      {tab === "Vehicle profiles" && <VehicleProfilesTab />}
      {tab === "Close types" && <CloseTypesTab />}
      {tab === "Integration keys" && <IntegrationKeysTab />}
      {tab === "Operators" && <OperatorsTab />}
      {tab === "General" && <GeneralTab />}
    </div>
  );
}
