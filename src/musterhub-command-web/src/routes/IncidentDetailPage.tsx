import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiFetchBlob } from "../auth/apiClient";
import { getStoredToken } from "../auth/tokenStore";
import { useToast } from "../components/ToastProvider";
import { IncidentMap } from "../components/IncidentMap";
import { LocationPicker } from "../components/LocationPicker";
import { ApplianceOfficerControl } from "../components/ApplianceOfficerControl";
import { PersonPicker } from "../components/PersonPicker";
import { CloseIncidentModal } from "../components/CloseIncidentModal";
import { AnnotateMapModal } from "../components/AnnotateMapModal";
import type {
  AddActionRequest, AddIncidentUpdateRequest, ApplianceStatus, DeviceDto, EmployeeDto, GeocodeResponseDto,
  IncidentActionKind, IncidentActionStatus, IncidentAttachmentDto, IncidentDto, IncidentUpdateType,
  MeResponse, OrganisationSettingsDto, ResourceKind, RouteResponseDto, SetApplianceEntry,
} from "../api/types";

const APPLIANCE_STATUSES: ApplianceStatus[] = ["Mobilised", "EnRoute", "OnScene", "StoodDown"];
const UPDATE_TYPES: IncidentUpdateType[] = ["General", "Hazard", "ResourceChange", "Note"];

const APPLIANCE_STATUS_STYLES: Record<ApplianceStatus, string> = {
  Mobilised: "bg-status-mobilised/15 text-status-mobilised",
  EnRoute: "bg-status-en-route/15 text-status-en-route",
  OnScene: "bg-status-on-scene/15 text-status-on-scene",
  StoodDown: "bg-status-stood-down/15 text-status-stood-down",
};

const RESOURCE_KIND_LABELS: Record<ResourceKind, string> = {
  Appliance: "Appliance",
  OfficerVehicle: "Officer",
  Specialist: "Specialist",
};

function AttendancePanel({ incident }: { incident: IncidentDto }) {
  const [editing, setEditing] = useState(false);
  const [rows, setRows] = useState<SetApplianceEntry[]>([]);
  const [newSectorName, setNewSectorName] = useState("");
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const employeesQuery = useQuery({ queryKey: ["employees"], queryFn: () => apiFetch<EmployeeDto[]>("/employees") });
  const employees = employeesQuery.data ?? [];
  const meQuery = useQuery({ queryKey: ["me"], queryFn: () => apiFetch<MeResponse>("/me") });
  const canManageSectors = meQuery.data?.isIncidentCommander ?? false;

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["incident", incident.id] });

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
      invalidate();
      showToast("Attendance updated");
      setEditing(false);
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const addSectorMutation = useMutation({
    mutationFn: (name: string) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/sectors`, { method: "POST", body: JSON.stringify({ name }) }),
    onSuccess: () => { invalidate(); setNewSectorName(""); },
    onError: (error) => showToast(error.message, "error"),
  });

  const deleteSectorMutation = useMutation({
    mutationFn: (sectorId: string) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/sectors/${sectorId}`, { method: "DELETE" }),
    onSuccess: () => invalidate(),
    onError: (error) => showToast(error.message, "error"),
  });

  const assignSectorMutation = useMutation({
    mutationFn: ({ applianceId, sectorId }: { applianceId: string; sectorId: string | null }) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/appliances/${applianceId}/sector`, {
        method: "PATCH",
        body: JSON.stringify({ sectorId }),
      }),
    onSuccess: () => invalidate(),
    onError: (error) => showToast(error.message, "error"),
  });

  const setResourceKindMutation = useMutation({
    mutationFn: ({ applianceId, resourceKind }: { applianceId: string; resourceKind: ResourceKind }) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/appliances/${applianceId}/resource-kind`, {
        method: "PATCH",
        body: JSON.stringify({ resourceKind }),
      }),
    onSuccess: () => invalidate(),
    onError: (error) => showToast(error.message, "error"),
  });

  // Unassigned last, and only shown if it actually has anyone in it -- an
  // incident with every appliance sectored shouldn't show an empty
  // "Unassigned" header. Root sectors only (no parentId) -- once a real
  // multi-level hierarchy exists (see IncidentHierarchyPage), a nested
  // node like "Sector 1 Commander" showing up as its own flat group here
  // too would just be a confusing near-duplicate of "Sector 1". This quick
  // card stays a shallow glance; the actual tree lives on its own screen.
  const rootSectors = incident.sectors.filter((s) => !s.parentId);
  const rootSectorIds = new Set(rootSectors.map((s) => s.id));
  const sectorGroups = [
    ...rootSectors.map((s) => ({ sector: s, appliances: incident.appliances.filter((a) => a.sectorId === s.id) })),
    { sector: null, appliances: incident.appliances.filter((a) => !a.sectorId || !rootSectorIds.has(a.sectorId)) },
  ].filter((g) => g.sector !== null || g.appliances.length > 0);

  return (
    <div className="rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card">
      <div className="flex items-center justify-between">
        <h2 className="text-card-title font-semibold text-(--content-primary)">Attendance</h2>
        <div className="flex items-center gap-3">
          <Link to={`/incidents/${incident.id}/hierarchy`} className="text-body text-brand-primary">
            View hierarchy
          </Link>
          {!editing && (
            <button type="button" onClick={startEditing} className="text-body text-brand-primary">
              Edit
            </button>
          )}
        </div>
      </div>

      {!editing && (
        <div className="mt-3 flex flex-col gap-4">
          {incident.appliances.length === 0 && (
            <p className="text-body text-(--content-secondary)">No appliances attending yet.</p>
          )}
          {sectorGroups.map((group) => (
            <div key={group.sector?.id ?? "unassigned"}>
              <div className="flex items-center justify-between">
                <h3 className="text-caption font-semibold uppercase tracking-wide text-(--content-secondary)">
                  {group.sector?.name ?? "Unassigned"}
                </h3>
                {group.sector && canManageSectors && (
                  <button
                    type="button"
                    onClick={() => deleteSectorMutation.mutate(group.sector!.id)}
                    className="text-caption text-(--content-secondary)"
                  >
                    Remove sector
                  </button>
                )}
              </div>
              <div className="mt-1.5 flex flex-col gap-2">
                {group.appliances.map((a) => (
                  <div key={a.id} className="flex items-center justify-between gap-2">
                    <div className="flex flex-col">
                      <span className="text-body font-medium text-(--content-primary)">{a.callsign}</span>
                      {a.locationUpdatedAtUtc && (
                        <span className="text-caption text-(--content-secondary)">
                          Last seen {new Date(a.locationUpdatedAtUtc).toLocaleTimeString()}
                        </span>
                      )}
                      <ApplianceOfficerControl incidentId={incident.id} appliance={a} employees={employees} />
                    </div>
                    <div className="flex items-center gap-2">
                      <select
                        value={a.resourceKind}
                        onChange={(e) => setResourceKindMutation.mutate({ applianceId: a.id, resourceKind: e.target.value as ResourceKind })}
                        className="rounded-lg border border-(--surface-border) px-2 py-1 text-caption"
                      >
                        {(Object.keys(RESOURCE_KIND_LABELS) as ResourceKind[]).map((k) => (
                          <option key={k} value={k}>{RESOURCE_KIND_LABELS[k]}</option>
                        ))}
                      </select>
                      <select
                        value={a.sectorId ?? ""}
                        onChange={(e) => assignSectorMutation.mutate({ applianceId: a.id, sectorId: e.target.value || null })}
                        className="rounded-lg border border-(--surface-border) px-2 py-1 text-caption"
                      >
                        <option value="">Unassigned</option>
                        {incident.sectors.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                      </select>
                      <span className={`rounded-full px-2 py-0.5 text-caption font-semibold ${APPLIANCE_STATUS_STYLES[a.status]}`}>
                        {a.status}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ))}
          {incident.appliances.length > 0 && canManageSectors && (
            <form
              className="flex gap-2"
              onSubmit={(e) => {
                e.preventDefault();
                if (newSectorName.trim()) addSectorMutation.mutate(newSectorName.trim());
              }}
            >
              <input
                value={newSectorName}
                onChange={(e) => setNewSectorName(e.target.value)}
                placeholder="New sector name"
                className="flex-1 rounded-lg border border-(--surface-border) px-2 py-1 text-body"
              />
              <button
                type="submit"
                disabled={addSectorMutation.isPending}
                className="text-body text-brand-primary disabled:opacity-60"
              >
                + Add sector
              </button>
            </form>
          )}
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

// One synthesized entry kind per row the timeline can show -- the two
// "created"/"closed" rows are synthesized client-side from Incident's own
// timestamps (no backend event exists for them), everything else comes
// straight off an IncidentUpdateDto. Keeping one discriminated type for
// both means the render loop below doesn't need two different code paths.
type TimelineEntry = {
  id: string;
  kind: "created" | "resourceChange" | "actionChange" | "hazard" | "note" | "general" | "closed";
  text: string;
  caption: string;
  timestamp: string;
  // Only General/Hazard updates are ever acknowledgeable -- a
  // ResourceChange/ActionChange line is a status log entry, not something
  // anyone needs to confirm they've seen, and "created"/"closed" are
  // synthesized client-side (no backend row to acknowledge in the first
  // place).
  acknowledgeable: boolean;
  acknowledgedAtUtc: string | null;
  acknowledgedByName: string | null;
  // Set only for real IncidentUpdate rows -- lets the Reply control target
  // a specific message, and lets a reply show what it's replying to.
  updateId: string | null;
  replyToUpdateId: string | null;
};

// "created" deliberately isn't brand-secondary (#0B1F3A) -- that's a
// light-theme text colour, near-black itself, so a dot painted with it is
// essentially invisible against the dark theme's own near-black page
// background (confirmed live on the tablet, which shares this same
// token). --content-secondary is already redefined per theme rather than
// a single static hex, which is the actual requirement for a colour used
// on a small filled dot rather than a full-width surface.
const TIMELINE_DOT_STYLES: Record<TimelineEntry["kind"], string> = {
  created: "bg-(--content-secondary)",
  resourceChange: "bg-status-mobilised",
  actionChange: "bg-brand-primary",
  hazard: "bg-status-hazard",
  note: "bg-(--content-secondary)",
  general: "bg-(--content-secondary)",
  closed: "bg-status-closed",
};

function updateKind(updateType: IncidentUpdateType): TimelineEntry["kind"] {
  if (updateType === "Hazard") return "hazard";
  if (updateType === "ResourceChange") return "resourceChange";
  if (updateType === "ActionChange") return "actionChange";
  if (updateType === "Note") return "note";
  return "general";
}

function buildTimeline(incident: IncidentDto): TimelineEntry[] {
  const entries: TimelineEntry[] = [
    {
      id: "created", kind: "created", text: `Incident created: ${incident.incidentType}`, caption: incident.orgUnitName,
      timestamp: incident.startedAtUtc, acknowledgeable: false, acknowledgedAtUtc: null, acknowledgedByName: null,
      updateId: null, replyToUpdateId: null,
    },
    ...incident.updates.map((u): TimelineEntry => ({
      id: u.id,
      kind: updateKind(u.updateType),
      text: u.text,
      // authorName first regardless of source -- a tablet's crew note is
      // tagged with its own device callsign (TabletIncidentsController.AddNote),
      // so this reads as "KV57P1", not a bare "Crew note" indistinguishable
      // from every other appliance's. Only a null authorName (a resource
      // change auto-logged with no note text of its own) falls back to the
      // generic per-source label.
      caption: u.updateType === "Hazard" ? "HAZARD" : (u.authorName ?? (u.source === "Crew" ? "Crew" : "Control Room")),
      timestamp: u.createdAtUtc,
      acknowledgeable: u.updateType === "General" || u.updateType === "Hazard",
      acknowledgedAtUtc: u.acknowledgedAtUtc,
      acknowledgedByName: u.acknowledgedByName,
      updateId: u.id,
      replyToUpdateId: u.replyToUpdateId,
    })),
  ];
  if (incident.closedAtUtc) {
    entries.push({
      id: "closed", kind: "closed",
      text: incident.closeTypeCode ? `Incident closed (${incident.closeTypeCode} - ${incident.closeTypeName})` : "Incident closed",
      caption: incident.status, timestamp: incident.closedAtUtc,
      acknowledgeable: false, acknowledgedAtUtc: null, acknowledgedByName: null,
      updateId: null, replyToUpdateId: null,
    });
  }
  // Oldest first, top to bottom -- reads as the incident's own story in
  // order, which is the point for a commander picking it up mid-way
  // through (handover, sectorisation) rather than scanning for the latest.
  // TimelinePanel offers a toggle for anyone who'd rather scan newest-first.
  return entries.sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime());
}

// "created"/"closed" are structural bookends, not an event type someone
// would want to filter away -- they stay visible regardless of which
// filter chips are active.
const FILTERABLE_KINDS = ["general", "hazard", "resourceChange", "actionChange", "note"] as const satisfies readonly TimelineEntry["kind"][];
const TIMELINE_FILTER_LABELS: Record<(typeof FILTERABLE_KINDS)[number], string> = {
  general: "General",
  hazard: "Hazard",
  resourceChange: "Resource",
  actionChange: "Task",
  note: "Note",
};

function TimelinePanel({ incident }: { incident: IncidentDto }) {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const [replyingToId, setReplyingToId] = useState<string | null>(null);
  const [replyText, setReplyText] = useState("");
  const [newestFirst, setNewestFirst] = useState(false);
  const [activeKinds, setActiveKinds] = useState<Set<(typeof FILTERABLE_KINDS)[number]>>(new Set(FILTERABLE_KINDS));

  const toggleKind = (kind: (typeof FILTERABLE_KINDS)[number]) => {
    setActiveKinds((prev) => {
      const next = new Set(prev);
      if (next.has(kind)) next.delete(kind); else next.add(kind);
      return next;
    });
  };

  // Same cache key RootLayout's account block already queries -- shares
  // that result rather than firing a second /me request just to attribute
  // an acknowledgement to the logged-in operator's own name.
  const meQuery = useQuery({ queryKey: ["me"], queryFn: () => apiFetch<MeResponse>("/me") });

  const addUpdateMutation = useMutation({
    mutationFn: (request: AddIncidentUpdateRequest) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/updates`, { method: "POST", body: JSON.stringify(request) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incident", incident.id] });
      showToast("Update added");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const replyMutation = useMutation({
    mutationFn: (request: AddIncidentUpdateRequest) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/updates`, { method: "POST", body: JSON.stringify(request) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incident", incident.id] });
      setReplyingToId(null);
      setReplyText("");
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const acknowledgeMutation = useMutation({
    mutationFn: (updateId: string) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/updates/${updateId}/acknowledge`, {
        method: "POST",
        body: JSON.stringify({ acknowledgedByName: meQuery.data?.displayName ?? null }),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["incident", incident.id] }),
    onError: (error) => showToast(error.message, "error"),
  });

  const timeline = buildTimeline(incident);
  // Replies stay in the same flat chronological list rather than a
  // separate thread view -- this just resolves what a reply's own quoted
  // preview should say. Built from the unfiltered/unreordered timeline so a
  // reply still resolves correctly even if its parent is filtered out or
  // the list is showing newest-first.
  const entryById = new Map(timeline.map((entry) => [entry.updateId, entry]));

  const visibleTimeline = timeline.filter(
    (entry) => entry.kind === "created" || entry.kind === "closed" || activeKinds.has(entry.kind),
  );
  const orderedTimeline = newestFirst ? [...visibleTimeline].reverse() : visibleTimeline;

  return (
    <div className="rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-card-title font-semibold text-(--content-primary)">Timeline</h2>
        <div className="flex items-center rounded-lg border border-(--surface-border) text-caption">
          <button
            type="button"
            onClick={() => setNewestFirst(false)}
            className={`rounded-l-lg px-2 py-1 font-medium ${!newestFirst ? "bg-brand-primary text-white" : "text-(--content-secondary)"}`}
          >
            Oldest first
          </button>
          <button
            type="button"
            onClick={() => setNewestFirst(true)}
            className={`rounded-r-lg px-2 py-1 font-medium ${newestFirst ? "bg-brand-primary text-white" : "text-(--content-secondary)"}`}
          >
            Newest first
          </button>
        </div>
      </div>

      <div className="mt-2 flex flex-wrap gap-1.5">
        {FILTERABLE_KINDS.map((kind) => {
          const active = activeKinds.has(kind);
          return (
            <button
              key={kind}
              type="button"
              onClick={() => toggleKind(kind)}
              className={`rounded-full border px-2.5 py-0.5 text-caption font-medium ${
                active
                  ? "border-brand-primary bg-brand-primary/10 text-brand-primary"
                  : "border-(--surface-border) text-(--content-secondary)"
              }`}
            >
              {TIMELINE_FILTER_LABELS[kind]}
            </button>
          );
        })}
      </div>

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

      {/* The rail is one absolutely-positioned line behind a column of
          dots, not a border-left on each row -- a per-row border leaves a
          visible seam at every gap; one continuous line reads as a single
          connected timeline instead of a stack of separately-bordered
          cards. */}
      <div className="relative mt-4 flex flex-col gap-4">
        <div className="absolute top-1 bottom-1 left-[5px] w-px bg-(--surface-border)" aria-hidden="true" />
        {orderedTimeline.map((entry) => (
          <div key={entry.id} className="relative flex gap-3 pl-6">
            <span className={`absolute top-1 left-0 h-2.5 w-2.5 rounded-full ring-2 ring-(--surface) ${TIMELINE_DOT_STYLES[entry.kind]}`} />
            <div className="flex-1">
              <div className="flex items-center justify-between text-caption text-(--content-secondary)">
                <span className={entry.kind === "hazard" ? "font-semibold text-status-hazard" : undefined}>{entry.caption}</span>
                <span>{new Date(entry.timestamp).toLocaleString()}</span>
              </div>
              {entry.replyToUpdateId && (
                <p className="mt-0.5 truncate text-caption text-(--content-secondary)">
                  &#8618; replying to: {entryById.get(entry.replyToUpdateId)?.text ?? "a since-removed message"}
                </p>
              )}
              <p className="mt-0.5 text-body text-(--content-primary)">{entry.text}</p>
              {entry.acknowledgeable && (
                entry.acknowledgedAtUtc ? (
                  <p className="mt-1 text-caption text-(--content-secondary)">
                    Acknowledged by {entry.acknowledgedByName} at {new Date(entry.acknowledgedAtUtc).toLocaleTimeString()}
                  </p>
                ) : (
                  <button
                    type="button"
                    disabled={acknowledgeMutation.isPending}
                    onClick={() => acknowledgeMutation.mutate(entry.id)}
                    className="mt-1 text-caption font-semibold text-brand-primary disabled:opacity-60"
                  >
                    Acknowledge
                  </button>
                )
              )}
              {entry.updateId && (
                replyingToId === entry.updateId ? (
                  <form
                    className="mt-2 flex gap-2"
                    onSubmit={(e) => {
                      e.preventDefault();
                      if (!replyText.trim()) return;
                      replyMutation.mutate({ text: replyText.trim(), replyToUpdateId: entry.updateId });
                    }}
                  >
                    <input
                      value={replyText}
                      onChange={(ev) => setReplyText(ev.target.value)}
                      placeholder="Reply..."
                      autoFocus
                      className="flex-1 rounded-lg border border-(--surface-border) px-2 py-1 text-caption"
                    />
                    <button
                      type="submit"
                      disabled={replyMutation.isPending}
                      className="text-caption font-semibold text-brand-primary disabled:opacity-60"
                    >
                      Send
                    </button>
                    <button
                      type="button"
                      onClick={() => { setReplyingToId(null); setReplyText(""); }}
                      className="text-caption text-(--content-secondary)"
                    >
                      Cancel
                    </button>
                  </form>
                ) : (
                  <button
                    type="button"
                    onClick={() => { setReplyingToId(entry.updateId); setReplyText(""); }}
                    className="mt-1 text-caption text-(--content-secondary)"
                  >
                    Reply
                  </button>
                )
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

const ACTION_KIND_LABELS: Record<IncidentActionKind, string> = {
  Task: "Task",
  ResourceRequest: "Resource Request",
};

const ACTION_STATUS_STYLES: Record<IncidentActionStatus, string> = {
  Open: "bg-status-mobilised/15 text-status-mobilised",
  Acknowledged: "bg-status-en-route/15 text-status-en-route",
  Completed: "bg-status-on-scene/15 text-status-on-scene",
  Declined: "bg-status-hazard/15 text-status-hazard",
};

// One entity for both -- a task and a resource request are the same
// shape (a directed ask with a status lifecycle and an assignee), see
// IncidentAction's own comment on the API side. Every status change also
// lands in the Timeline via QueueActionChangeUpdate, so this card and the
// Timeline never drift apart on "what actually happened."
function ActionsPanel({ incident }: { incident: IncidentDto }) {
  const [adding, setAdding] = useState(false);
  const [kind, setKind] = useState<IncidentActionKind>("Task");
  const [text, setText] = useState("");
  const [assignedToEmployeeId, setAssignedToEmployeeId] = useState<string | null>(null);
  const [assignedToName, setAssignedToName] = useState<string | null>(null);
  const [sectorId, setSectorId] = useState("");
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const meQuery = useQuery({ queryKey: ["me"], queryFn: () => apiFetch<MeResponse>("/me") });
  const employeesQuery = useQuery({ queryKey: ["employees"], queryFn: () => apiFetch<EmployeeDto[]>("/employees") });
  const employees = employeesQuery.data ?? [];

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["incident", incident.id] });

  const resetForm = () => {
    setKind("Task");
    setText("");
    setAssignedToEmployeeId(null);
    setAssignedToName(null);
    setSectorId("");
    setAdding(false);
  };

  const addMutation = useMutation({
    mutationFn: (request: AddActionRequest) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/actions`, { method: "POST", body: JSON.stringify(request) }),
    onSuccess: () => { invalidate(); resetForm(); },
    onError: (error) => showToast(error.message, "error"),
  });

  const acknowledgeMutation = useMutation({
    mutationFn: (actionId: string) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/actions/${actionId}/acknowledge`, {
        method: "POST",
        body: JSON.stringify({ acknowledgedByName: meQuery.data?.displayName ?? null }),
      }),
    onSuccess: () => invalidate(),
    onError: (error) => showToast(error.message, "error"),
  });

  const resolveMutation = useMutation({
    mutationFn: ({ actionId, status }: { actionId: string; status: IncidentActionStatus }) =>
      apiFetch<IncidentDto>(`/incidents/${incident.id}/actions/${actionId}/resolve`, {
        method: "POST",
        body: JSON.stringify({ status, resolvedByName: meQuery.data?.displayName ?? null }),
      }),
    onSuccess: () => invalidate(),
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <div className="rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card">
      <div className="flex items-center justify-between">
        <h2 className="text-card-title font-semibold text-(--content-primary)">Tasks &amp; Requests</h2>
        {!adding && (
          <button type="button" onClick={() => setAdding(true)} className="text-body text-brand-primary">
            + Add
          </button>
        )}
      </div>

      {incident.actions.length === 0 && !adding && (
        <p className="mt-3 text-body text-(--content-secondary)">Nothing raised yet.</p>
      )}

      <div className="mt-3 flex flex-col gap-3">
        {incident.actions.map((a) => (
          <div key={a.id} className="rounded-lg border border-(--surface-border) p-3">
            <div className="flex items-start justify-between gap-2">
              <div>
                <span className="text-caption font-semibold uppercase tracking-wide text-(--content-secondary)">
                  {ACTION_KIND_LABELS[a.kind]}
                </span>
                <p className="text-body text-(--content-primary)">{a.text}</p>
                {a.assignedToName && (
                  <p className="text-caption text-(--content-secondary)">Assigned to {a.assignedToName}</p>
                )}
              </div>
              <span className={`shrink-0 rounded-full px-2 py-0.5 text-caption font-semibold ${ACTION_STATUS_STYLES[a.status]}`}>
                {a.status}
              </span>
            </div>
            <div className="mt-2 flex items-center gap-3 text-caption">
              {a.status === "Open" && (
                <button
                  type="button"
                  disabled={acknowledgeMutation.isPending}
                  onClick={() => acknowledgeMutation.mutate(a.id)}
                  className="font-semibold text-brand-primary disabled:opacity-60"
                >
                  Acknowledge
                </button>
              )}
              {(a.status === "Open" || a.status === "Acknowledged") && (
                <>
                  <button
                    type="button"
                    disabled={resolveMutation.isPending}
                    onClick={() => resolveMutation.mutate({ actionId: a.id, status: "Completed" })}
                    className="font-semibold text-status-on-scene disabled:opacity-60"
                  >
                    Complete
                  </button>
                  <button
                    type="button"
                    disabled={resolveMutation.isPending}
                    onClick={() => resolveMutation.mutate({ actionId: a.id, status: "Declined" })}
                    className="text-(--content-secondary) disabled:opacity-60"
                  >
                    Decline
                  </button>
                </>
              )}
              {a.resolvedByName && a.resolvedAtUtc && (
                <span className="text-(--content-secondary)">
                  {a.status} by {a.resolvedByName} at {new Date(a.resolvedAtUtc).toLocaleTimeString()}
                </span>
              )}
            </div>
          </div>
        ))}
      </div>

      {adding && (
        <div className="mt-3 flex flex-col gap-2 rounded-lg border border-(--surface-border) p-3">
          <select
            value={kind}
            onChange={(e) => setKind(e.target.value as IncidentActionKind)}
            className="rounded-lg border border-(--surface-border) px-2 py-1 text-body"
          >
            {(Object.keys(ACTION_KIND_LABELS) as IncidentActionKind[]).map((k) => (
              <option key={k} value={k}>{ACTION_KIND_LABELS[k]}</option>
            ))}
          </select>
          <input
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={kind === "Task" ? "Task description" : "What's needed?"}
            className="rounded-lg border border-(--surface-border) px-2 py-1 text-body"
            autoFocus
          />
          <PersonPicker
            employees={employees}
            employeeId={assignedToEmployeeId}
            name={assignedToName}
            placeholder="Assign to..."
            onChange={(empId, name) => { setAssignedToEmployeeId(empId); setAssignedToName(name); }}
          />
          <select
            value={sectorId}
            onChange={(e) => setSectorId(e.target.value)}
            className="rounded-lg border border-(--surface-border) px-2 py-1 text-body"
          >
            <option value="">No specific sector</option>
            {incident.sectors.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
          <div className="flex justify-end gap-2">
            <button type="button" onClick={resetForm} className="rounded-lg px-3 py-1.5 text-body text-(--content-secondary)">
              Cancel
            </button>
            <button
              type="button"
              disabled={!text.trim() || addMutation.isPending}
              onClick={() => addMutation.mutate({
                kind, text: text.trim(), raisedByName: meQuery.data?.displayName ?? null,
                assignedToEmployeeId, assignedToName, sectorId: sectorId || null,
              })}
              className="rounded-lg bg-brand-primary px-3 py-1.5 text-body font-semibold text-white disabled:opacity-60"
            >
              Raise
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function formatFileSize(bytes: number) {
  return bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)}MB` : `${Math.round(bytes / 1024)}KB`;
}

function isImage(contentType: string) {
  return contentType.startsWith("image/");
}

// A plain <img src="/api/..."> or <a href="/api/..."> can't carry the
// Bearer token this app authenticates with, so both the thumbnail and the
// "open" click fetch the blob through apiFetchBlob and hand the browser an
// object URL instead. The thumbnail's object URL is cached for the
// component's lifetime; "open" always fetches its own (short-lived, freed
// once the new tab has loaded it) rather than reusing the thumbnail one,
// since revoking on unmount here shouldn't yank the file out from under a
// tab the user is still looking at.
function AttachmentThumbnail({ attachment }: { attachment: IncidentAttachmentDto }) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const { showToast } = useToast();

  useEffect(() => {
    if (!isImage(attachment.contentType)) return;
    let cancelled = false;
    let url: string | null = null;
    apiFetchBlob(`/incident-attachments/${attachment.id}`).then((blob) => {
      if (cancelled) return;
      url = URL.createObjectURL(blob);
      setObjectUrl(url);
    }).catch((error) => showToast(error.message, "error"));
    return () => {
      cancelled = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [attachment.id, attachment.contentType, showToast]);

  const open = async () => {
    try {
      const blob = await apiFetchBlob(`/incident-attachments/${attachment.id}`);
      const url = URL.createObjectURL(blob);
      window.open(url, "_blank");
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
    } catch (error) {
      showToast(error instanceof Error ? error.message : String(error), "error");
    }
  };

  return (
    <button type="button" onClick={open} className="block w-full">
      {objectUrl ? (
        <img src={objectUrl} alt={attachment.fileName} className="h-24 w-full rounded object-cover" />
      ) : (
        <div className="flex h-24 w-full items-center justify-center rounded bg-(--surface-page) text-caption text-(--content-secondary)">
          {attachment.fileName.split(".").pop()?.toUpperCase()}
        </div>
      )}
    </button>
  );
}

function PhotosPanel({ incident }: { incident: IncidentDto }) {
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["incident", incident.id] });

  const uploadMutation = useMutation({
    mutationFn: (file: File) => {
      const body = new FormData();
      body.append("file", file);
      return apiFetch<IncidentAttachmentDto>(`/incidents/${incident.id}/attachments`, { method: "POST", body });
    },
    onSuccess: invalidate,
    onError: (error) => showToast(error.message, "error"),
  });

  const deleteMutation = useMutation({
    mutationFn: (attachmentId: string) => apiFetch<void>(`/incident-attachments/${attachmentId}`, { method: "DELETE" }),
    onSuccess: invalidate,
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <div className="rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card">
      <div className="flex items-center justify-between">
        <h2 className="text-card-title font-semibold text-(--content-primary)">Photos &amp; Documents</h2>
        <button
          type="button"
          disabled={uploadMutation.isPending}
          onClick={() => fileInputRef.current?.click()}
          className="rounded-lg bg-brand-primary px-3 py-1.5 text-body font-semibold text-white disabled:opacity-60"
        >
          {uploadMutation.isPending ? "Uploading..." : "Upload"}
        </button>
        <input
          ref={fileInputRef}
          type="file"
          accept="image/jpeg,image/png,image/heic,image/heif,image/webp,application/pdf"
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) uploadMutation.mutate(file);
            e.target.value = "";
          }}
        />
      </div>
      {incident.attachments.length === 0 && (
        <p className="mt-3 text-body text-(--content-secondary)">No photos or documents attached yet.</p>
      )}
      <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4">
        {incident.attachments.map((a) => (
          <div key={a.id} className="flex flex-col gap-1 rounded-lg border border-(--surface-border) p-2">
            <AttachmentThumbnail attachment={a} />
            <p className="truncate text-caption text-(--content-primary)" title={a.fileName}>{a.fileName}</p>
            <p className="text-caption text-(--content-secondary)">
              {formatFileSize(a.sizeBytes)}{a.uploadedByName && ` · ${a.uploadedByName}`}
            </p>
            <button
              type="button"
              onClick={() => { if (confirm(`Delete "${a.fileName}"?`)) deleteMutation.mutate(a.id); }}
              className="text-caption text-status-hazard"
            >
              Delete
            </button>
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

// Vision-fed incidents arrive with coordinates already; the manual "New
// incident" path is the one place they can be missing, and previously the
// map just silently disappeared with no explanation. This always shows
// something -- the map, or an explicit prompt to add one -- and the same
// picker doubles as a way to correct a wrong pin later.
function LocationPanel({ incident, route, onRouteChange }: {
  incident: IncidentDto;
  route: { appliances: DeviceDto[]; routePoints: [number, number][] | null };
  onRouteChange: (route: { appliances: DeviceDto[]; routePoints: [number, number][] | null }) => void;
}) {
  const [editing, setEditing] = useState(false);
  const [draftLat, setDraftLat] = useState<number | null>(incident.latitude);
  const [draftLng, setDraftLng] = useState<number | null>(incident.longitude);
  // Session-only, defaults visible -- not persisted across a reload or a
  // different incident, same as the tablet's own map-hide toggle.
  const [mapVisible, setMapVisible] = useState(true);
  const [annotateOpen, setAnnotateOpen] = useState(false);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  // Same query key Setup > General uses -- shares its cache rather than
  // re-fetching, and stays fresh if an Operator changes the radius there.
  const settingsQuery = useQuery({
    queryKey: ["organisation-settings"],
    queryFn: () => apiFetch<OrganisationSettingsDto>("/organisation-settings"),
  });
  const geofenceRadiusMeters = settingsQuery.data?.geofenceRadiusMeters;

  const startEditing = () => {
    setDraftLat(incident.latitude);
    setDraftLng(incident.longitude);
    setEditing(true);
  };

  const saveLocationMutation = useMutation({
    mutationFn: () => apiFetch<IncidentDto>(`/incidents/${incident.id}`, {
      method: "PATCH",
      body: JSON.stringify({ latitude: draftLat, longitude: draftLng }),
    }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incident", incident.id] });
      showToast("Location updated");
      setEditing(false);
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const locateMutation = useMutation({
    mutationFn: (query: string) => apiFetch<GeocodeResponseDto>(`/geocode?query=${encodeURIComponent(query)}`),
    onSuccess: (result) => {
      if (!result.found || result.latitude == null || result.longitude == null) {
        showToast("Couldn't find that address -- click the map to set it instead", "error");
        return;
      }
      setDraftLat(result.latitude);
      setDraftLng(result.longitude);
    },
    onError: (error) => showToast(error.message, "error"),
  });

  const hasLocation = incident.latitude != null && incident.longitude != null;

  if (editing) {
    return (
      <div className="rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <h2 className="text-card-title font-semibold text-(--content-primary)">Set location</h2>
          {incident.address && (
            <button
              type="button"
              disabled={locateMutation.isPending}
              onClick={() => locateMutation.mutate(incident.address!)}
              className="rounded-lg border border-(--surface-border) px-3 py-1.5 text-caption text-(--content-primary) disabled:opacity-60"
            >
              {locateMutation.isPending ? "Locating..." : `Locate "${incident.address}"`}
            </button>
          )}
        </div>
        <LocationPicker latitude={draftLat} longitude={draftLng} onChange={(lat, lng) => { setDraftLat(lat); setDraftLng(lng); }} allowClear={false} />
        <div className="flex justify-end gap-2">
          <button type="button" onClick={() => setEditing(false)} className="rounded-lg px-3 py-1.5 text-body text-(--content-secondary)">
            Cancel
          </button>
          <button
            type="button"
            disabled={saveLocationMutation.isPending || draftLat == null || draftLng == null}
            onClick={() => saveLocationMutation.mutate()}
            className="rounded-lg bg-brand-primary px-3 py-1.5 text-body font-semibold text-white disabled:opacity-60"
          >
            Save
          </button>
        </div>
      </div>
    );
  }

  if (!hasLocation) {
    return (
      <div className="rounded-card border border-(--surface-border) bg-(--surface) p-4 shadow-card flex items-center justify-between">
        <p className="text-body text-(--content-secondary)">No location set for this incident yet.</p>
        <button type="button" onClick={startEditing} className="text-body font-semibold text-brand-primary">
          Add location
        </button>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      {mapVisible && (
        <IncidentMap
          latitude={incident.latitude!}
          longitude={incident.longitude!}
          label={incident.address ?? incident.incidentType}
          appliances={route.appliances.map((d) => ({ label: d.label, latitude: d.currentLatitude!, longitude: d.currentLongitude! }))}
          routePoints={route.routePoints ?? undefined}
          geofenceRadiusMeters={geofenceRadiusMeters}
          onAnnotate={() => setAnnotateOpen(true)}
        />
      )}
      <AnnotateMapModal
        open={annotateOpen}
        onOpenChange={setAnnotateOpen}
        incidentId={incident.id}
        incidentReference={incident.externalReference}
        latitude={incident.latitude!}
        longitude={incident.longitude!}
        label={incident.address ?? incident.incidentType}
        appliances={route.appliances.map((d) => ({ label: d.label, latitude: d.currentLatitude!, longitude: d.currentLongitude! }))}
        geofenceRadiusMeters={geofenceRadiusMeters}
      />
      <div className="flex items-center justify-between">
        {/* RoutingPanel keeps rendering with the map hidden -- distance/
            duration is still useful info on its own, only the visual map
            itself is what someone might want out of the way. */}
        <RoutingPanel incident={incident} onRouteChange={onRouteChange} />
        <div className="flex items-center gap-3">
          <button type="button" onClick={() => setMapVisible((v) => !v)} className="text-caption text-(--content-secondary)">
            {mapVisible ? "Hide map" : "Show map"}
          </button>
          <button type="button" onClick={startEditing} className="text-caption text-brand-primary">
            Edit location
          </button>
        </div>
      </div>
    </div>
  );
}

type DetailTab = "overview" | "tasks" | "timeline" | "photos";

// Plain buttons, not a component library -- this is the same idea as
// every other bit of UI in this file, just toggling which panel below
// renders instead of toggling a form's visibility. The badge is a count
// of "would you want to know this without clicking in," not a generic
// item count -- see its callers for what that means per tab.
function TabButton({ label, active, badge, badgeUrgent, onClick }: {
  label: string;
  active: boolean;
  badge?: number;
  badgeUrgent?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex items-center gap-2 border-b-2 px-1 pb-2 text-body font-semibold ${
        active ? "border-brand-primary text-brand-primary" : "border-transparent text-(--content-secondary)"
      }`}
    >
      {label}
      {!!badge && (
        <span
          className={`rounded-full px-2 py-0.5 text-caption font-semibold text-white ${
            badgeUrgent ? "bg-status-hazard" : "bg-(--content-secondary)"
          }`}
        >
          {badge}
        </span>
      )}
    </button>
  );
}

export function IncidentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const [activeTab, setActiveTab] = useState<DetailTab>("overview");

  const incidentQuery = useQuery({
    queryKey: ["incident", id],
    queryFn: () => apiFetch<IncidentDto>(`/incidents/${id}`),
    refetchInterval: 20_000,
  });
  const meQuery = useQuery({ queryKey: ["me"], queryFn: () => apiFetch<MeResponse>("/me") });
  const canManageIncident = meQuery.data?.isIncidentCommander ?? false;

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
          <Link
            // The print page opens in a new tab, which gets its own empty
            // sessionStorage -- carry the access token in the URL hash so
            // that tab's own resolveAccessToken() can pick it up, same
            // mechanism core's own handoff into Command already uses.
            to={`/incidents/${incident.id}/print#token=${getStoredToken() ?? ""}`}
            target="_blank"
            className="rounded-lg border border-(--surface-border) px-3 py-1.5 text-body text-(--content-primary)"
          >
            Export
          </Link>
          {incident.status === "Open" && canManageIncident && (
            <CloseIncidentModal incidentId={incident.id} />
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
          {incident.status !== "Cancelled" && canManageIncident && (
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

      <LocationPanel incident={incident} route={route} onRouteChange={setRoute} />

      {(() => {
        const openActionsCount = incident.actions.filter((a) => a.status === "Open").length;
        const unacknowledged = incident.updates.filter(
          (u) => (u.updateType === "General" || u.updateType === "Hazard") && !u.acknowledgedAtUtc,
        );
        return (
          <div className="flex gap-6 border-b border-(--surface-border)">
            <TabButton label="Overview" active={activeTab === "overview"} onClick={() => setActiveTab("overview")} />
            <TabButton
              label="Tasks & Requests"
              active={activeTab === "tasks"}
              badge={openActionsCount}
              onClick={() => setActiveTab("tasks")}
            />
            <TabButton
              label="Timeline"
              active={activeTab === "timeline"}
              badge={unacknowledged.length}
              badgeUrgent={unacknowledged.some((u) => u.updateType === "Hazard")}
              onClick={() => setActiveTab("timeline")}
            />
            <TabButton
              label="Photos"
              active={activeTab === "photos"}
              badge={incident.attachments.length}
              onClick={() => setActiveTab("photos")}
            />
          </div>
        );
      })()}

      {activeTab === "overview" && <AttendancePanel incident={incident} />}
      {activeTab === "tasks" && <ActionsPanel incident={incident} />}
      {activeTab === "timeline" && <TimelinePanel incident={incident} />}
      {activeTab === "photos" && <PhotosPanel incident={incident} />}
    </div>
  );
}
