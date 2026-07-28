// Mirrors MusterHubCommand.Api's Contracts/*.cs exactly. IncidentStatus /
// ApplianceStatus / IncidentUpdateType / IncidentUpdateSource serialize as
// strings on the wire (Program.cs registers a global JsonStringEnumConverter),
// not the ordinal ints Rota/Skills' own DTOs use.

export type CommandOperatorTier = "ControlRoom" | "CommandSupport" | "IncidentCommander";

export interface MeResponse {
  organisationId: string;
  employeeId: string | null;
  displayName: string | null;
  isOperator: boolean;
  isBootstrapping: boolean;
  operatorTier: CommandOperatorTier | null;
  isIncidentCommander: boolean;
}

export interface OrgUnitDto {
  id: string;
  name: string;
  code: string | null;
  orgUnitTypeName: string;
  parentId: string | null;
}

export interface EmployeeDto {
  id: string;
  displayName: string;
  employeeNumber: string | null;
  operatorTier: CommandOperatorTier | null;
}

export type IncidentStatus = "Open" | "Closed" | "Cancelled";
export type ApplianceStatus = "Mobilised" | "EnRoute" | "OnScene" | "StoodDown";
export type ResourceKind = "Appliance" | "OfficerVehicle" | "Specialist";
export type IncidentUpdateSource = "ControlRoom" | "Crew";
export type IncidentUpdateType = "General" | "Hazard" | "ResourceChange" | "Note" | "ActionChange";
export type IncidentActionKind = "Task" | "ResourceRequest";
export type IncidentActionStatus = "Open" | "Acknowledged" | "Completed" | "Declined";

export interface IncidentApplianceDto {
  id: string;
  callsign: string;
  status: ApplianceStatus;
  resourceKind: ResourceKind;
  sectorId: string | null;
  officerInChargeEmployeeId: string | null;
  // Always the display name -- resolved server-side the same way
  // IncidentSectorDto.personInChargeName is.
  officerInChargeName: string | null;
  latitude: number | null;
  longitude: number | null;
  locationUpdatedAtUtc: string | null;
  updatedAtUtc: string;
}

export interface IncidentSectorDto {
  id: string;
  name: string;
  sortOrder: number;
  parentId: string | null;
  personInChargeEmployeeId: string | null;
  // Always the display name -- resolved server-side from the linked
  // Employee when personInChargeEmployeeId is set, or the free-text name
  // otherwise.
  personInChargeName: string | null;
}

export interface IncidentUpdateDto {
  id: string;
  source: IncidentUpdateSource;
  authorName: string | null;
  authorEmployeeId: string | null;
  text: string;
  updateType: IncidentUpdateType;
  acknowledgedAtUtc: string | null;
  acknowledgedByName: string | null;
  replyToUpdateId: string | null;
  createdAtUtc: string;
}

export type IncidentObjectiveStatus = "Open" | "Achieved";

// raisedByName/achievedByName are always resolved server-side from the
// caller's own session (the signed-in operator, or a tablet's
// DeviceCallsign) -- never caller-supplied, unlike
// IncidentActionDto.raisedByName.
export interface IncidentObjectiveDto {
  id: string;
  text: string;
  status: IncidentObjectiveStatus;
  source: IncidentUpdateSource;
  raisedByName: string;
  raisedByEmployeeId: string | null;
  achievedAtUtc: string | null;
  achievedByName: string | null;
  createdAtUtc: string;
}

export type IncidentRiskLevel = "Low" | "Medium" | "High";
export type IncidentRiskStatus = "Identified" | "Controlled";

// raisedByName/reviewedByName follow the same server-resolved-only rule as
// IncidentObjectiveDto. Unlike an objective, raising a risk also lands a
// Hazard entry in the Timeline.
export interface IncidentRiskDto {
  id: string;
  description: string;
  riskLevel: IncidentRiskLevel;
  controlMeasure: string | null;
  status: IncidentRiskStatus;
  source: IncidentUpdateSource;
  raisedByName: string;
  raisedByEmployeeId: string | null;
  reviewedAtUtc: string | null;
  reviewedByName: string | null;
  createdAtUtc: string;
}

// Status isn't stored -- Overdue is computed server-side at read time from
// exitedAtUtc/whistleAtUtc vs now, so it's always correct against the
// clock rather than needing a background job.
export type BaWearerStatus = "InBa" | "Exited" | "Overdue";

// The three BA Operating Procedures stages, per National Operational
// Guidance -- I: entry control on the appliance/bridgehead with no
// guideline; II: a guideline used; III: main and emergency lines.
export type BaStage = "I" | "II" | "III";

// Read-only from the web console's own point of view -- the ECO is
// physically at the entry control point with a tablet, so every write for
// a BA board lives there, not here. The web console only ever displays
// this (see IncidentDto.baEntryControlPoints), for oversight.
export interface BaWearerDto {
  id: string;
  name: string;
  cylinderPressureBar: number;
  enteredAtUtc: string;
  whistleAtUtc: string;
  exitedAtUtc: string | null;
  status: BaWearerStatus;
}

export interface BaTeamDto {
  id: string;
  name: string;
  teamLeader: string;
  commsChannel: string | null;
  briefing: string | null;
  equipment: string | null;
  wearers: BaWearerDto[];
}

export interface BaEntryControlPointDto {
  id: string;
  name: string;
  stage: BaStage;
  // Always false here -- device ownership only means something to the
  // tablet that's scoped by it; the web console sees every point.
  isOwnedByThisDevice: boolean;
  teams: BaTeamDto[];
}

// assignedToName/raisedByName are always the display name -- resolved
// server-side the same way IncidentSectorDto.personInChargeName is.
export interface IncidentActionDto {
  id: string;
  kind: IncidentActionKind;
  text: string;
  status: IncidentActionStatus;
  source: IncidentUpdateSource;
  raisedByName: string | null;
  raisedByEmployeeId: string | null;
  assignedToEmployeeId: string | null;
  assignedToName: string | null;
  sectorId: string | null;
  acknowledgedAtUtc: string | null;
  acknowledgedByName: string | null;
  resolvedAtUtc: string | null;
  resolvedByName: string | null;
  createdAtUtc: string;
}

export interface IncidentDto {
  id: string;
  externalReference: string;
  incidentType: string;
  description: string | null;
  address: string | null;
  latitude: number | null;
  longitude: number | null;
  orgUnitId: string;
  orgUnitName: string;
  status: IncidentStatus;
  startedAtUtc: string;
  closedAtUtc: string | null;
  updatedAtUtc: string;
  closeTypeId: string | null;
  closeTypeCode: string | null;
  closeTypeName: string | null;
  closeActionsTaken: string | null;
  closeOutcome: string | null;
  appliances: IncidentApplianceDto[];
  updates: IncidentUpdateDto[];
  sectors: IncidentSectorDto[];
  objectives: IncidentObjectiveDto[];
  risks: IncidentRiskDto[];
  actions: IncidentActionDto[];
  baEntryControlPoints: BaEntryControlPointDto[];
  attachments: IncidentAttachmentDto[];
  // The server's own clock at response time -- the tablet uses this to
  // correct BA Entry Control's live countdowns against its own (not
  // fully trustworthy) clock. Unused here, but part of the real payload.
  serverNowUtc: string;
}

export interface IncidentCloseTypeDto {
  id: string;
  code: string;
  name: string;
}

export interface IncidentAttachmentDto {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedAtUtc: string;
  uploadedByName: string | null;
}

export interface IncidentSummaryDto {
  id: string;
  externalReference: string;
  incidentType: string;
  address: string | null;
  orgUnitId: string;
  orgUnitName: string;
  status: IncidentStatus;
  startedAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateIncidentRequest {
  externalReference: string;
  incidentType: string;
  description?: string | null;
  address?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  stationCode: string;
  startedAtUtc?: string | null;
}

export interface UpdateIncidentRequest {
  incidentType?: string | null;
  description?: string | null;
  address?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  stationCode?: string | null;
  status?: IncidentStatus | null;
}

export interface SetApplianceEntry {
  callsign: string;
  status: ApplianceStatus;
}

export interface AddIncidentUpdateRequest {
  authorName?: string | null;
  text: string;
  updateType?: IncidentUpdateType;
  replyToUpdateId?: string | null;
}

export interface AcknowledgeUpdateRequest {
  acknowledgedByName?: string | null;
}

// Only text -- raisedByName/raisedByEmployeeId are resolved server-side by
// the caller, not accepted here the way AddActionRequest.raisedByName is.
export interface AddObjectiveRequest {
  text: string;
}

// Same trust model as AddObjectiveRequest -- controlMeasure is optional at
// raise time, can be filled in once a plan's actually in place.
export interface AddRiskRequest {
  description: string;
  riskLevel: IncidentRiskLevel;
  controlMeasure?: string | null;
}

// No AddBaEntryRequest here -- BA Entry Control is a tablet-only write
// surface (see the API's own TabletIncidentsController comment), the web
// console only ever reads BaEntryControlPointDto.

// Kind isn't direction-locked -- see the API's own AddActionRequest comment.
export interface AddActionRequest {
  kind: IncidentActionKind;
  text: string;
  raisedByName?: string | null;
  assignedToEmployeeId?: string | null;
  assignedToName?: string | null;
  sectorId?: string | null;
}

export interface AcknowledgeActionRequest {
  acknowledgedByName?: string | null;
}

export interface ResolveActionRequest {
  status: IncidentActionStatus;
  resolvedByName?: string | null;
}

export interface AddSectorRequest {
  name: string;
  parentId?: string | null;
  personInChargeEmployeeId?: string | null;
  personInChargeName?: string | null;
}

// Full-replace, not a sparse patch -- see the API's own UpdateSectorRequest
// comment for why (parentId/personInCharge are themselves nullable, so
// there's no spare bit left to mean "leave this alone").
export interface UpdateSectorRequest {
  name: string;
  parentId: string | null;
  personInChargeEmployeeId: string | null;
  personInChargeName: string | null;
}

export interface AssignSectorRequest {
  sectorId: string | null;
}

export interface SetResourceKindRequest {
  resourceKind: ResourceKind;
}

// Full-replace, same reasoning as UpdateSectorRequest.
export interface SetApplianceOfficerRequest {
  officerInChargeEmployeeId: string | null;
  officerInChargeName: string | null;
}

export interface DeviceDto {
  id: string;
  label: string;
  orgUnitId: string;
  orgUnitName: string;
  vehicleProfileId: string | null;
  vehicleProfileName: string | null;
  callsign: string | null;
  currentLatitude: number | null;
  currentLongitude: number | null;
  locationUpdatedAtUtc: string | null;
  createdAtUtc: string;
  lastSeenAtUtc: string | null;
  isActive: boolean;
}

export interface VehicleProfileDto {
  id: string;
  name: string;
  maxWeightTonnes: number | null;
  maxHeightMetres: number | null;
  maxWidthMetres: number | null;
}

export interface SaveVehicleProfileRequest {
  name: string;
  maxWeightTonnes?: number | null;
  maxHeightMetres?: number | null;
  maxWidthMetres?: number | null;
}

export interface RoutePointDto {
  latitude: number;
  longitude: number;
}

export interface RouteResponseDto {
  available: boolean;
  unavailableReason: string | null;
  distanceMeters: number | null;
  durationSeconds: number | null;
  points: RoutePointDto[] | null;
}

export interface OrganisationSettingsDto {
  geofenceRadiusMeters: number;
  baEntryControlEnabled: boolean;
}

export interface GeocodeResponseDto {
  found: boolean;
  latitude: number | null;
  longitude: number | null;
  displayName: string | null;
}

export interface CreateDeviceResponse {
  id: string;
  label: string;
  pairingToken: string;
}

export interface IntegrationApiKeyDto {
  id: string;
  label: string;
  createdAtUtc: string;
  lastUsedAtUtc: string | null;
  isActive: boolean;
}

export interface CreateIntegrationApiKeyResponse {
  id: string;
  label: string;
  apiKey: string;
}
