// Mirrors MusterHubCommand.Api's Contracts/*.cs exactly. IncidentStatus /
// ApplianceStatus / IncidentUpdateType / IncidentUpdateSource serialize as
// strings on the wire (Program.cs registers a global JsonStringEnumConverter),
// not the ordinal ints Rota/Skills' own DTOs use.

export interface MeResponse {
  organisationId: string;
  employeeId: string | null;
  displayName: string | null;
  isOperator: boolean;
  isBootstrapping: boolean;
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
  isOperator: boolean;
}

export type IncidentStatus = "Open" | "Closed" | "Cancelled";
export type ApplianceStatus = "Mobilised" | "EnRoute" | "OnScene" | "StoodDown";
export type IncidentUpdateSource = "ControlRoom" | "Crew";
export type IncidentUpdateType = "General" | "Hazard" | "ResourceChange" | "Note";

export interface IncidentApplianceDto {
  id: string;
  callsign: string;
  status: ApplianceStatus;
  updatedAtUtc: string;
}

export interface IncidentUpdateDto {
  id: string;
  source: IncidentUpdateSource;
  authorName: string | null;
  authorEmployeeId: string | null;
  text: string;
  updateType: IncidentUpdateType;
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
  appliances: IncidentApplianceDto[];
  updates: IncidentUpdateDto[];
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
