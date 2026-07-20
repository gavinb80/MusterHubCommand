import { MapContainer, Marker, TileLayer } from "react-leaflet";
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

export function IncidentMap({ latitude, longitude, label }: { latitude: number; longitude: number; label: string }) {
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
      <Marker position={[latitude, longitude]} icon={markerIcon} title={label} />
    </MapContainer>
  );
}
