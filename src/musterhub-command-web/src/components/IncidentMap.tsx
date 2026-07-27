import { Circle, MapContainer, Marker, Polyline, TileLayer, Tooltip, useMap } from "react-leaflet";
import L from "leaflet";
import markerIconUrl from "leaflet/dist/images/marker-icon.png";
import markerIcon2xUrl from "leaflet/dist/images/marker-icon-2x.png";
import markerShadowUrl from "leaflet/dist/images/marker-shadow.png";
import type { ApplianceStatus } from "../api/types";

// Leaflet's default marker icon references its own image paths relative to
// the CSS file, which breaks once Vite bundles/hashes assets -- every
// react-leaflet app needs this fix, not something specific to Command.
const markerIcon = L.icon({
  iconUrl: markerIconUrl,
  iconRetinaUrl: markerIcon2xUrl,
  shadowUrl: markerShadowUrl,
  iconSize: [25, 41],
  iconAnchor: [12, 41],
});

// A plain coloured dot, not another pin -- an appliance's position needs to
// read as visually distinct from the incident's own marker at a glance,
// not as "a second incident." Colour keys off status (same tokens as the
// status pills elsewhere) so en-route vs on-scene reads without a click;
// the permanent callsign tooltip next to each dot is what actually answers
// "which appliance is this" when more than one is visible.
function buildApplianceIcon(color: string) {
  return L.divIcon({
    className: "",
    html: `<div style="width:16px;height:16px;border-radius:50%;background:${color};border:2px solid white;box-shadow:0 1px 3px rgba(0,0,0,0.4);"></div>`,
    iconSize: [16, 16],
    iconAnchor: [8, 8],
  });
}

const APPLIANCE_STATUS_ICONS: Record<ApplianceStatus, L.DivIcon> = {
  Mobilised: buildApplianceIcon("var(--color-status-mobilised)"),
  EnRoute: buildApplianceIcon("var(--color-status-en-route)"),
  OnScene: buildApplianceIcon("var(--color-status-on-scene)"),
  StoodDown: buildApplianceIcon("var(--color-status-stood-down)"),
};

export interface AppliancePosition {
  label: string;
  status: ApplianceStatus;
  latitude: number;
  longitude: number;
}

// Recenter/Expand rendered via useMap() rather than passed a ref -- this is
// the react-leaflet way to reach the underlying Leaflet map instance from
// inside the tree MapContainer owns. Positioned below Leaflet's own
// top-left zoom control so the two don't overlap.
function MapControls({
  latitude, longitude, appliances, onAnnotate,
}: {
  latitude: number;
  longitude: number;
  appliances?: AppliancePosition[];
  onAnnotate?: () => void;
}) {
  const map = useMap();

  const recenter = () => {
    const points: [number, number][] = [[latitude, longitude], ...(appliances ?? []).map((a): [number, number] => [a.latitude, a.longitude])];
    if (points.length === 1) {
      map.setView(points[0], 16);
    } else {
      map.fitBounds(L.latLngBounds(points), { padding: [32, 32] });
    }
  };

  return (
    <div className="leaflet-top leaflet-right" style={{ marginTop: 76 }}>
      <div className="leaflet-control leaflet-bar flex flex-col overflow-hidden bg-white">
        <button
          type="button"
          title="Recenter"
          onClick={recenter}
          className="flex h-8 w-8 items-center justify-center text-body text-(--content-primary) hover:bg-(--surface-page)"
        >
          &#8982;
        </button>
        {onAnnotate && (
          <button
            type="button"
            title="Expand & annotate"
            onClick={onAnnotate}
            className="flex h-8 w-8 items-center justify-center border-t border-(--surface-border) text-body text-(--content-primary) hover:bg-(--surface-page)"
          >
            &#10021;
          </button>
        )}
      </div>
    </div>
  );
}

export function IncidentMap({
  latitude, longitude, label, appliances, routePoints, geofenceRadiusMeters,
  interactive = false, heightClassName = "h-64", onAnnotate,
}: {
  latitude: number;
  longitude: number;
  label: string;
  appliances?: AppliancePosition[];
  routePoints?: [number, number][];
  geofenceRadiusMeters?: number;
  // The small inline map on the incident page guards against scroll- and
  // gesture-hijacking (page-scroll-wheel zoom, pinch-zoom, double-click
  // zoom firing by accident) -- the larger annotate view opts into those
  // explicitly. Dragging isn't part of that: a click-and-drag pan is a
  // deliberate gesture, not one that fires by accident, so it stays on
  // even when the map is otherwise inert -- Leaflet always shows its
  // zoom +/- buttons regardless of this flag, and a map you can zoom but
  // not pan reads as broken.
  interactive?: boolean;
  heightClassName?: string;
  onAnnotate?: () => void;
}) {
  return (
    <MapContainer
      center={[latitude, longitude]}
      zoom={16}
      className={`${heightClassName} w-full`}
      scrollWheelZoom={interactive}
      dragging
      doubleClickZoom={interactive}
      touchZoom={interactive}
    >
      <TileLayer
        attribution="&copy; OpenStreetMap contributors"
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
        crossOrigin="anonymous"
      />
      {geofenceRadiusMeters != null && (
        <Circle
          center={[latitude, longitude]}
          radius={geofenceRadiusMeters}
          pathOptions={{ color: "#FF453A", weight: 1, fillColor: "#FF453A", fillOpacity: 0.08, dashArray: "4 4" }}
        />
      )}
      <Marker position={[latitude, longitude]} icon={markerIcon} title={label} />
      {appliances?.map((a, i) => (
        <Marker key={i} position={[a.latitude, a.longitude]} icon={APPLIANCE_STATUS_ICONS[a.status]}>
          <Tooltip permanent direction="right" offset={[8, 0]} className="appliance-label-tooltip" opacity={1}>
            {a.label}
          </Tooltip>
        </Marker>
      ))}
      {routePoints && routePoints.length > 1 && (
        <Polyline positions={routePoints} pathOptions={{ color: "#0A84FF", weight: 4, opacity: 0.8 }} />
      )}
      <MapControls latitude={latitude} longitude={longitude} appliances={appliances} onAnnotate={onAnnotate} />
    </MapContainer>
  );
}
