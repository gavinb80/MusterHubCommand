import { useEffect } from "react";
import { MapContainer, Marker, TileLayer, useMap, useMapEvents } from "react-leaflet";
import L from "leaflet";
import markerIconUrl from "leaflet/dist/images/marker-icon.png";
import markerIcon2xUrl from "leaflet/dist/images/marker-icon-2x.png";
import markerShadowUrl from "leaflet/dist/images/marker-shadow.png";

const markerIcon = L.icon({
  iconUrl: markerIconUrl,
  iconRetinaUrl: markerIcon2xUrl,
  shadowUrl: markerShadowUrl,
  iconSize: [25, 41],
  iconAnchor: [12, 41],
});

// Devon & Somerset's rough centre -- stations don't carry their own
// coordinates yet, so this is a reasonable region-wide default until they do.
const DEFAULT_CENTER: [number, number] = [50.85, -3.6];
const DEFAULT_ZOOM = 9;

function ClickCapture({ onPick }: { onPick: (lat: number, lng: number) => void }) {
  useMapEvents({ click: (e) => onPick(e.latlng.lat, e.latlng.lng) });
  return null;
}

// MapContainer's center/zoom props only apply on first mount -- react-leaflet
// doesn't move an already-mounted map when they change later, so a pin set
// externally (the "Locate" button geocoding an address) never brought the
// map's view along with it. This drives the view imperatively whenever the
// coordinates change, from whatever source.
function Recenter({ latitude, longitude }: { latitude: number | null; longitude: number | null }) {
  const map = useMap();
  useEffect(() => {
    if (latitude != null && longitude != null) {
      map.flyTo([latitude, longitude], Math.max(map.getZoom(), 15), { duration: 0.5 });
    }
  }, [latitude, longitude]);
  return null;
}

// Click-to-place location capture, used anywhere an incident needs
// coordinates set or corrected by hand -- Vision-fed incidents arrive with
// real coordinates already, but the manual "New incident" path (and fixing
// a wrong pin) has no other way to produce them.
export function LocationPicker({ latitude, longitude, onChange, allowClear = true }: {
  latitude: number | null;
  longitude: number | null;
  onChange: (latitude: number | null, longitude: number | null) => void;
  // The incident PATCH endpoint only ever sets lat/lng when non-null -- it
  // can't clear one back to unset. Callers editing an already-created
  // incident's location should pass false, or "Clear" then Save silently
  // does nothing.
  allowClear?: boolean;
}) {
  const hasPin = latitude != null && longitude != null;
  const center: [number, number] = hasPin ? [latitude, longitude] : DEFAULT_CENTER;

  return (
    <div className="flex flex-col gap-2">
      <MapContainer center={center} zoom={hasPin ? 15 : DEFAULT_ZOOM} className="h-48 w-full rounded-lg" scrollWheelZoom>
        <TileLayer attribution="&copy; OpenStreetMap contributors" url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
        {hasPin && <Marker position={[latitude, longitude]} icon={markerIcon} />}
        <ClickCapture onPick={onChange} />
        <Recenter latitude={latitude} longitude={longitude} />
      </MapContainer>
      <div className="flex items-center justify-between text-caption text-(--content-secondary)">
        <span>{hasPin ? `${latitude.toFixed(5)}, ${longitude.toFixed(5)} -- click the map to move it` : "Click the map to set a location"}</span>
        {hasPin && allowClear && (
          <button type="button" onClick={() => onChange(null, null)} className="text-brand-primary">Clear</button>
        )}
      </div>
    </div>
  );
}
