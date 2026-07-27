import { Circle, MapContainer, Marker, Polyline, TileLayer, useMap } from "react-leaflet";
import L from "leaflet";
import markerIconUrl from "leaflet/dist/images/marker-icon.png";
import markerIcon2xUrl from "leaflet/dist/images/marker-icon-2x.png";
import markerShadowUrl from "leaflet/dist/images/marker-shadow.png";

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
// not as "a second incident."
const applianceIcon = L.divIcon({
  className: "",
  html: '<div style="width:16px;height:16px;border-radius:50%;background:#0A84FF;border:2px solid white;box-shadow:0 1px 3px rgba(0,0,0,0.4);"></div>',
  iconSize: [16, 16],
  iconAnchor: [8, 8],
});

export interface AppliancePosition {
  label: string;
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
  // The small inline map on the incident page is deliberately inert (no
  // scroll-hijacking, no accidental drag) -- the larger annotate view is
  // the one place free pan/zoom is wanted, so it opts in explicitly.
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
      dragging={interactive}
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
        <Marker key={i} position={[a.latitude, a.longitude]} icon={applianceIcon} title={a.label} />
      ))}
      {routePoints && routePoints.length > 1 && (
        <Polyline positions={routePoints} pathOptions={{ color: "#0A84FF", weight: 4, opacity: 0.8 }} />
      )}
      <MapControls latitude={latitude} longitude={longitude} appliances={appliances} onAnnotate={onAnnotate} />
    </MapContainer>
  );
}
