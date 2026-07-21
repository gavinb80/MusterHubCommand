import { Circle, MapContainer, Marker, Polyline, TileLayer } from "react-leaflet";
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

export function IncidentMap({
  latitude, longitude, label, appliances, routePoints, geofenceRadiusMeters,
}: {
  latitude: number;
  longitude: number;
  label: string;
  appliances?: AppliancePosition[];
  routePoints?: [number, number][];
  geofenceRadiusMeters?: number;
}) {
  return (
    <MapContainer
      center={[latitude, longitude]}
      zoom={16}
      className="h-64 w-full"
      scrollWheelZoom={false}
    >
      <TileLayer
        attribution="&copy; OpenStreetMap contributors"
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
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
    </MapContainer>
  );
}
