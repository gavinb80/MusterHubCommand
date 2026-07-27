import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import { apiFetch, apiFetchBlob } from "../auth/apiClient";
import type { IncidentAttachmentDto, IncidentDto } from "../api/types";

const ACTION_KIND_LABELS: Record<string, string> = { Task: "Task", ResourceRequest: "Resource Request" };

// A plain <img src="/api/..."> can't carry this app's Bearer token, so each
// photo fetches its own bytes through apiFetchBlob and builds an object URL
// -- deliberately its own effect, not folded into the page's main incident
// query, which stays untouched (staleTime: Infinity below) precisely
// because a refetch there has bitten this page before.
function PrintAttachment({ attachment }: { attachment: IncidentAttachmentDto }) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const isImage = attachment.contentType.startsWith("image/");

  useEffect(() => {
    if (!isImage) return;
    let cancelled = false;
    let url: string | null = null;
    apiFetchBlob(`/incident-attachments/${attachment.id}`).then((blob) => {
      if (cancelled) return;
      url = URL.createObjectURL(blob);
      setObjectUrl(url);
    }).catch(() => {});
    return () => {
      cancelled = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [attachment.id, isImage]);

  return (
    <div className="flex flex-col gap-1">
      {isImage && objectUrl ? (
        <img src={objectUrl} alt={attachment.fileName} className="h-40 w-full rounded border border-(--surface-border) object-cover" />
      ) : (
        <div className="flex h-40 w-full items-center justify-center rounded border border-(--surface-border) text-caption text-(--content-secondary)">
          {attachment.fileName.split(".").pop()?.toUpperCase()}
        </div>
      )}
      <p className="truncate text-caption text-(--content-secondary)" title={attachment.fileName}>{attachment.fileName}</p>
    </div>
  );
}

// A dedicated route rather than a print stylesheet on the live incident
// page -- the live page is full of buttons/forms/dropdowns that make no
// sense on paper, and @media print rules to hide all of that piecemeal
// would be more fragile than just rendering a clean, read-only report from
// the same data. "Print" here means the browser's own print dialog, whose
// "Save as PDF" destination is the actual export -- no PDF library needed.
export function IncidentPrintPage() {
  const { id } = useParams<{ id: string }>();
  const incidentQuery = useQuery({
    queryKey: ["incident", id],
    queryFn: () => apiFetch<IncidentDto>(`/incidents/${id}`),
    enabled: !!id,
    // A print report is a one-time snapshot, not a live view -- no
    // refetch-on-focus. Without this, opening the OS print dialog (which
    // blurs/refocuses the tab) refetches mid-print, and if the token
    // happens to have expired by then, the resulting 401 wipes the whole
    // page via main.tsx's global session-expiry handler -- exactly the
    // "prints fine, but leave the print dialog open a few seconds and the
    // page goes blank" bug this was hit by.
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });

  if (incidentQuery.isLoading) return <p className="p-6 text-body">Loading...</p>;
  const incident = incidentQuery.data;
  if (!incident) return <p className="p-6 text-body">Incident not found.</p>;

  const rootSectors = incident.sectors.filter((s) => !s.parentId);
  const rootSectorIds = new Set(rootSectors.map((s) => s.id));
  const sectorGroups = [
    ...rootSectors.map((s) => ({ sector: s, appliances: incident.appliances.filter((a) => a.sectorId === s.id) })),
    { sector: null, appliances: incident.appliances.filter((a) => !a.sectorId || !rootSectorIds.has(a.sectorId)) },
  ].filter((g) => g.sector !== null || g.appliances.length > 0);

  // Oldest first, unfiltered -- a printed report is the full record, not
  // the at-a-glance live view, so none of TimelinePanel's sort/filter
  // controls apply here.
  const timeline = [...incident.updates].sort(
    (a, b) => new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime(),
  );

  return (
    <div className="mx-auto max-w-3xl p-8 text-(--content-primary)">
      <div className="no-print mb-6 flex items-center justify-between">
        <Link to={`/incidents/${incident.id}`} className="text-body text-brand-primary">
          &larr; Back to incident
        </Link>
        <button
          type="button"
          onClick={() => window.print()}
          className="rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white"
        >
          Print / Save as PDF
        </button>
      </div>

      <h1 className="text-title font-bold">{incident.incidentType}</h1>
      <p className="text-body text-(--content-secondary)">
        {incident.externalReference} &middot; {incident.address ?? "No address"} &middot; {incident.orgUnitName}
      </p>
      <p className="mt-1 text-body text-(--content-secondary)">
        Status: {incident.status} &middot; Started {new Date(incident.startedAtUtc).toLocaleString()}
        {incident.closedAtUtc && <> &middot; Closed {new Date(incident.closedAtUtc).toLocaleString()}</>}
      </p>
      {incident.description && <p className="mt-2 text-body">{incident.description}</p>}

      {incident.status === "Closed" && (incident.closeTypeCode || incident.closeActionsTaken || incident.closeOutcome) && (
        <>
          <h2 className="mt-8 border-b border-(--surface-border) pb-1 text-card-title font-semibold">Close Summary</h2>
          <dl className="mt-2 flex flex-col gap-2">
            {incident.closeTypeCode && (
              <div>
                <dt className="text-caption font-semibold uppercase tracking-wide text-(--content-secondary)">Close type</dt>
                <dd className="text-body">{incident.closeTypeCode} - {incident.closeTypeName}</dd>
              </div>
            )}
            {incident.closeActionsTaken && (
              <div>
                <dt className="text-caption font-semibold uppercase tracking-wide text-(--content-secondary)">Actions taken</dt>
                <dd className="text-body">{incident.closeActionsTaken}</dd>
              </div>
            )}
            {incident.closeOutcome && (
              <div>
                <dt className="text-caption font-semibold uppercase tracking-wide text-(--content-secondary)">Outcome</dt>
                <dd className="text-body">{incident.closeOutcome}</dd>
              </div>
            )}
          </dl>
        </>
      )}

      <h2 className="mt-8 border-b border-(--surface-border) pb-1 text-card-title font-semibold">Attendance</h2>
      {sectorGroups.length === 0 && <p className="mt-2 text-body text-(--content-secondary)">No appliances attended.</p>}
      {sectorGroups.map((group) => (
        <div key={group.sector?.id ?? "unassigned"} className="mt-3">
          <h3 className="text-caption font-semibold uppercase tracking-wide text-(--content-secondary)">
            {group.sector?.name ?? "Unassigned"}
            {group.sector?.personInChargeName && ` — Person in charge: ${group.sector.personInChargeName}`}
          </h3>
          <ul className="mt-1 flex flex-col gap-0.5">
            {group.appliances.map((a) => (
              <li key={a.id} className="text-body">
                {a.callsign} &middot; {a.status} &middot; {a.resourceKind}
                {a.officerInChargeName && ` — Officer: ${a.officerInChargeName}`}
              </li>
            ))}
          </ul>
        </div>
      ))}

      <h2 className="mt-8 border-b border-(--surface-border) pb-1 text-card-title font-semibold">Tasks &amp; Requests</h2>
      {incident.actions.length === 0 && <p className="mt-2 text-body text-(--content-secondary)">None raised.</p>}
      <ul className="mt-2 flex flex-col gap-2">
        {incident.actions.map((a) => (
          <li key={a.id} className="text-body">
            <span className="font-semibold">{ACTION_KIND_LABELS[a.kind] ?? a.kind}:</span> {a.text} &middot; {a.status}
            {a.assignedToName && ` — Assigned to ${a.assignedToName}`}
          </li>
        ))}
      </ul>

      <h2 className="mt-8 border-b border-(--surface-border) pb-1 text-card-title font-semibold">Timeline</h2>
      <ul className="mt-2 flex flex-col gap-2">
        <li className="text-body">
          <span className="text-caption text-(--content-secondary)">{new Date(incident.startedAtUtc).toLocaleString()}</span>
          {" — "}Incident created: {incident.incidentType}
        </li>
        {timeline.map((u) => (
          <li key={u.id} className="text-body">
            <span className="text-caption text-(--content-secondary)">{new Date(u.createdAtUtc).toLocaleString()}</span>
            {" — "}
            <span className={u.updateType === "Hazard" ? "font-semibold" : undefined}>
              [{u.updateType === "Hazard" ? "HAZARD" : (u.authorName ?? u.source)}]
            </span>{" "}
            {u.text}
            {u.acknowledgedAtUtc && (
              <span className="text-caption text-(--content-secondary)">
                {" "}(acknowledged by {u.acknowledgedByName} at {new Date(u.acknowledgedAtUtc).toLocaleTimeString()})
              </span>
            )}
          </li>
        ))}
        {incident.closedAtUtc && (
          <li className="text-body">
            <span className="text-caption text-(--content-secondary)">{new Date(incident.closedAtUtc).toLocaleString()}</span>
            {" — "}Incident closed
            {incident.closeTypeCode && ` (${incident.closeTypeCode} - ${incident.closeTypeName})`}
          </li>
        )}
      </ul>

      {incident.attachments.length > 0 && (
        <>
          <h2 className="mt-8 border-b border-(--surface-border) pb-1 text-card-title font-semibold">Photos &amp; Documents</h2>
          <div className="mt-2 grid grid-cols-3 gap-3">
            {incident.attachments.map((a) => <PrintAttachment key={a.id} attachment={a} />)}
          </div>
        </>
      )}
    </div>
  );
}
