import { useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "../auth/apiClient";
import { useToast } from "../components/ToastProvider";
import { PersonPicker } from "../components/PersonPicker";
import { ApplianceOfficerControl } from "../components/ApplianceOfficerControl";
import type {
  AddSectorRequest, ApplianceStatus, EmployeeDto, IncidentApplianceDto, IncidentDto,
  IncidentSectorDto, MeResponse, UpdateSectorRequest,
} from "../api/types";

const APPLIANCE_STATUS_STYLES: Record<ApplianceStatus, string> = {
  Mobilised: "bg-status-mobilised/15 text-status-mobilised",
  EnRoute: "bg-status-en-route/15 text-status-en-route",
  OnScene: "bg-status-on-scene/15 text-status-on-scene",
  StoodDown: "bg-status-stood-down/15 text-status-stood-down",
};

function NodeEditor({ incident, node, defaultParentId, employees, onDone }: {
  incident: IncidentDto;
  node: IncidentSectorDto | null; // null = creating a new node
  defaultParentId?: string | null; // preset parent when creating (e.g. "+ Add child" under a specific node)
  employees: EmployeeDto[];
  onDone: () => void;
}) {
  const [name, setName] = useState(node?.name ?? "");
  const [parentId, setParentId] = useState<string | null>(node ? node.parentId : (defaultParentId ?? null));
  const [personInChargeEmployeeId, setPersonInChargeEmployeeId] = useState<string | null>(node?.personInChargeEmployeeId ?? null);
  const [personInChargeName, setPersonInChargeName] = useState<string | null>(node?.personInChargeName ?? null);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["incident", incident.id] });

  const saveMutation = useMutation({
    mutationFn: () => {
      if (node) {
        const body: UpdateSectorRequest = { name: name.trim(), parentId, personInChargeEmployeeId, personInChargeName };
        return apiFetch<IncidentDto>(`/incidents/${incident.id}/sectors/${node.id}`, { method: "PATCH", body: JSON.stringify(body) });
      }
      const body: AddSectorRequest = { name: name.trim(), parentId, personInChargeEmployeeId, personInChargeName };
      return apiFetch<IncidentDto>(`/incidents/${incident.id}/sectors`, { method: "POST", body: JSON.stringify(body) });
    },
    onSuccess: () => { invalidate(); onDone(); },
    onError: (error) => showToast(error.message, "error"),
  });

  // A node can't become its own parent, and the server itself rejects
  // moving a node under its own descendant -- this list only pre-filters
  // the obvious case (self) for a cleaner picker; the deeper cycle check
  // stays server-side, single source of truth.
  const parentOptions = incident.sectors.filter((s) => s.id !== node?.id);

  return (
    <div className="rounded-lg border border-(--surface-border) bg-(--surface) p-3 flex flex-col gap-2">
      <input
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder="Node name (e.g. Sector 1, Incident Commander)"
        className="rounded-lg border border-(--surface-border) px-2 py-1 text-body"
        autoFocus
      />
      <label className="flex flex-col gap-1 text-caption text-(--content-secondary)">
        Parent
        <select
          value={parentId ?? ""}
          onChange={(e) => setParentId(e.target.value || null)}
          className="rounded-lg border border-(--surface-border) px-2 py-1 text-body text-(--content-primary)"
        >
          <option value="">None (top of the tree)</option>
          {parentOptions.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
        </select>
      </label>
      <label className="flex flex-col gap-1 text-caption text-(--content-secondary)">
        Person in charge
        <PersonPicker
          employees={employees}
          employeeId={personInChargeEmployeeId}
          name={personInChargeName}
          onChange={(id, n) => { setPersonInChargeEmployeeId(id); setPersonInChargeName(n); }}
        />
      </label>
      <div className="flex justify-end gap-2">
        <button type="button" onClick={onDone} className="rounded-lg px-3 py-1.5 text-body text-(--content-secondary)">
          Cancel
        </button>
        <button
          type="button"
          disabled={!name.trim() || saveMutation.isPending}
          onClick={() => saveMutation.mutate()}
          className="rounded-lg bg-brand-primary px-3 py-1.5 text-body font-semibold text-white disabled:opacity-60"
        >
          Save
        </button>
      </div>
    </div>
  );
}

function TreeNode({ incident, node, childrenByParent, appliancesByNode, depth, employees, canManage }: {
  incident: IncidentDto;
  node: IncidentSectorDto;
  childrenByParent: Map<string | null, IncidentSectorDto[]>;
  appliancesByNode: Map<string, IncidentApplianceDto[]>;
  depth: number;
  employees: EmployeeDto[];
  canManage: boolean;
}) {
  const [editing, setEditing] = useState(false);
  const [addingChild, setAddingChild] = useState(false);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const children = childrenByParent.get(node.id) ?? [];
  const appliances = appliancesByNode.get(node.id) ?? [];

  const deleteMutation = useMutation({
    mutationFn: () => apiFetch<IncidentDto>(`/incidents/${incident.id}/sectors/${node.id}`, { method: "DELETE" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["incident", incident.id] }),
    onError: (error) => showToast(error.message, "error"),
  });

  if (editing) {
    return (
      <NodeEditor incident={incident} node={node} employees={employees} onDone={() => setEditing(false)} />
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="rounded-lg border border-(--surface-border) bg-(--surface) p-3 shadow-card">
        <div className="flex items-center justify-between gap-3">
          <div>
            <p className="text-body font-semibold text-(--content-primary)">{node.name}</p>
            {node.personInChargeName && (
              <p className="text-caption text-(--content-secondary)">Person in charge: {node.personInChargeName}</p>
            )}
            {appliances.length > 0 && (
              <div className="mt-1 flex flex-wrap gap-2">
                {appliances.map((a) => (
                  <div key={a.id} className="flex flex-col gap-0.5 rounded-lg border border-(--surface-border) px-2 py-1.5">
                    <span className="inline-flex items-center gap-1 text-caption">
                      <span className="font-medium text-(--content-primary)">{a.callsign}</span>
                      <span className={`rounded-full px-2 py-0.5 font-semibold ${APPLIANCE_STATUS_STYLES[a.status]}`}>{a.status}</span>
                    </span>
                    <ApplianceOfficerControl incidentId={incident.id} appliance={a} employees={employees} />
                  </div>
                ))}
              </div>
            )}
          </div>
          {canManage && (
            <div className="flex shrink-0 items-center gap-2 text-caption">
              <button type="button" onClick={() => setAddingChild(true)} className="text-brand-primary">+ Add child</button>
              <button type="button" onClick={() => setEditing(true)} className="text-(--content-secondary)">Edit</button>
              <button
                type="button"
                onClick={() => {
                  if (confirm(`Remove "${node.name}"? Any nodes beneath it go too -- attached appliances just become Unassigned.`)) {
                    deleteMutation.mutate();
                  }
                }}
                className="text-status-hazard"
              >
                Remove
              </button>
            </div>
          )}
        </div>
      </div>

      {addingChild && (
        <div className="ml-6">
          <NodeEditor
            incident={incident}
            node={null}
            defaultParentId={node.id}
            employees={employees}
            onDone={() => setAddingChild(false)}
          />
        </div>
      )}

      {children.length > 0 && (
        <div className="ml-6 flex flex-col gap-2 border-l border-(--surface-border) pl-4">
          {children.map((child) => (
            <TreeNode
              key={child.id}
              incident={incident}
              node={child}
              childrenByParent={childrenByParent}
              appliancesByNode={appliancesByNode}
              depth={depth + 1}
              employees={employees}
              canManage={canManage}
            />
          ))}
        </div>
      )}
    </div>
  );
}

export function IncidentHierarchyPage() {
  const { id } = useParams<{ id: string }>();
  const [addingRoot, setAddingRoot] = useState(false);

  const incidentQuery = useQuery({
    queryKey: ["incident", id],
    queryFn: () => apiFetch<IncidentDto>(`/incidents/${id}`),
    refetchInterval: 20_000,
  });
  const employeesQuery = useQuery({ queryKey: ["employees"], queryFn: () => apiFetch<EmployeeDto[]>("/employees") });
  const meQuery = useQuery({ queryKey: ["me"], queryFn: () => apiFetch<MeResponse>("/me") });

  const incident = incidentQuery.data;
  const employees = employeesQuery.data ?? [];
  const canManage = meQuery.data?.isIncidentCommander ?? false;

  const { roots, childrenByParent, appliancesByNode } = useMemo(() => {
    const childrenByParent = new Map<string | null, IncidentSectorDto[]>();
    const appliancesByNode = new Map<string, IncidentApplianceDto[]>();
    if (incident) {
      for (const sector of incident.sectors) {
        const key = sector.parentId ?? null;
        if (!childrenByParent.has(key)) childrenByParent.set(key, []);
        childrenByParent.get(key)!.push(sector);
      }
      for (const appliance of incident.appliances) {
        if (!appliance.sectorId) continue;
        if (!appliancesByNode.has(appliance.sectorId)) appliancesByNode.set(appliance.sectorId, []);
        appliancesByNode.get(appliance.sectorId)!.push(appliance);
      }
    }
    return { roots: childrenByParent.get(null) ?? [], childrenByParent, appliancesByNode };
  }, [incident]);

  if (incidentQuery.isLoading) return <p className="text-body text-(--content-secondary)">Loading...</p>;
  if (!incident) return <p className="text-body text-(--content-secondary)">Incident not found.</p>;

  return (
    <div className="flex flex-col gap-4">
      <Link to={`/incidents/${incident.id}`} className="text-body text-brand-primary">&larr; Back to {incident.incidentType}</Link>

      <div>
        <h1 className="text-page-title font-semibold text-(--content-primary)">Incident Hierarchy</h1>
        <p className="mt-1 text-body text-(--content-secondary)">
          Command structure for this incident: as flat or as deep as it actually needs to be.
        </p>
      </div>

      <div className="flex flex-col gap-3">
        {roots.length === 0 && !addingRoot && (
          <p className="text-body text-(--content-secondary)">No hierarchy set up for this incident yet.</p>
        )}
        {roots.map((root) => (
          <TreeNode
            key={root.id}
            incident={incident}
            node={root}
            childrenByParent={childrenByParent}
            appliancesByNode={appliancesByNode}
            depth={0}
            employees={employees}
            canManage={canManage}
          />
        ))}

        {canManage && (addingRoot ? (
          <NodeEditor incident={incident} node={null} employees={employees} onDone={() => setAddingRoot(false)} />
        ) : (
          <button type="button" onClick={() => setAddingRoot(true)} className="self-start text-body text-brand-primary">
            + Add top-level node
          </button>
        ))}
      </div>
    </div>
  );
}
