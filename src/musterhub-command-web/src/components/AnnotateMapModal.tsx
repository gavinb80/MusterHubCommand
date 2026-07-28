import { useRef, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import * as Dialog from "@radix-ui/react-dialog";
import html2canvas from "html2canvas";
import { apiFetch } from "../auth/apiClient";
import { useToast } from "./ToastProvider";
import { IncidentMap, type AppliancePosition } from "./IncidentMap";
import type { IncidentAttachmentDto } from "../api/types";

const PEN_COLORS = ["#FF453A", "#FFD60A", "#0A84FF", "#111111"];
const PEN_COLOR_NAMES: Record<string, string> = {
  "#FF453A": "Red", "#FFD60A": "Yellow", "#0A84FF": "Blue", "#111111": "Black",
};
const PEN_WIDTHS = [3, 7];

type Stroke = { color: string; width: number; points: { x: number; y: number }[] };

function drawStroke(ctx: CanvasRenderingContext2D, stroke: Stroke) {
  if (stroke.points.length === 0) return;
  ctx.strokeStyle = stroke.color;
  ctx.lineWidth = stroke.width;
  ctx.lineCap = "round";
  ctx.lineJoin = "round";
  ctx.beginPath();
  ctx.moveTo(stroke.points[0].x, stroke.points[0].y);
  for (const p of stroke.points.slice(1)) ctx.lineTo(p.x, p.y);
  ctx.stroke();
}

export function AnnotateMapModal({
  open, onOpenChange, incidentId, incidentReference, latitude, longitude, label, appliances, geofenceRadiusMeters,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  incidentId: string;
  incidentReference: string;
  latitude: number;
  longitude: number;
  label: string;
  appliances?: AppliancePosition[];
  geofenceRadiusMeters?: number;
}) {
  const [stage, setStage] = useState<"view" | "annotate">("view");
  const [color, setColor] = useState(PEN_COLORS[0]);
  const [width, setWidth] = useState(PEN_WIDTHS[0]);
  const [strokes, setStrokes] = useState<Stroke[]>([]);
  const mapContainerRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const baseImageRef = useRef<HTMLImageElement | null>(null);
  const drawingRef = useRef<Stroke | null>(null);
  const [capturing, setCapturing] = useState(false);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const reset = () => {
    setStage("view");
    setStrokes([]);
    baseImageRef.current = null;
  };

  const redraw = () => {
    const canvas = canvasRef.current;
    const image = baseImageRef.current;
    if (!canvas || !image) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
    for (const stroke of strokes) drawStroke(ctx, stroke);
  };

  const capture = async () => {
    if (!mapContainerRef.current) return;
    setCapturing(true);
    try {
      // OpenStreetMap's tile servers send Access-Control-Allow-Origin: *,
      // confirmed live -- useCORS lets html2canvas actually read the tile
      // pixels instead of producing a blank/tainted result.
      const captured = await html2canvas(mapContainerRef.current, { useCORS: true });
      const image = new Image();
      image.onload = () => {
        baseImageRef.current = image;
        setStrokes([]);
        setStage("annotate");
        requestAnimationFrame(() => {
          const canvas = canvasRef.current;
          if (!canvas) return;
          canvas.width = captured.width;
          canvas.height = captured.height;
          redraw();
        });
      };
      image.src = captured.toDataURL("image/png");
    } catch {
      showToast("Couldn't capture the map view", "error");
    } finally {
      setCapturing(false);
    }
  };

  const canvasPoint = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current!;
    const rect = canvas.getBoundingClientRect();
    return {
      x: ((e.clientX - rect.left) / rect.width) * canvas.width,
      y: ((e.clientY - rect.top) / rect.height) * canvas.height,
    };
  };

  const onPointerDown = (e: React.PointerEvent<HTMLCanvasElement>) => {
    (e.target as HTMLCanvasElement).setPointerCapture(e.pointerId);
    drawingRef.current = { color, width, points: [canvasPoint(e)] };
  };

  const onPointerMove = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const stroke = drawingRef.current;
    if (!stroke) return;
    stroke.points.push(canvasPoint(e));
    const ctx = canvasRef.current?.getContext("2d");
    if (ctx) drawStroke(ctx, { ...stroke, points: stroke.points.slice(-2) });
  };

  const onPointerUp = () => {
    const stroke = drawingRef.current;
    if (!stroke) return;
    drawingRef.current = null;
    setStrokes((prev) => [...prev, stroke]);
  };

  const undo = () => {
    setStrokes((prev) => prev.slice(0, -1));
    requestAnimationFrame(redraw);
  };

  const clearAnnotations = () => {
    setStrokes([]);
    requestAnimationFrame(redraw);
  };

  const download = () => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const a = document.createElement("a");
    a.href = canvas.toDataURL("image/png");
    a.download = `map-annotation-${incidentReference}-${Date.now()}.png`;
    a.click();
  };

  const saveMutation = useMutation({
    mutationFn: () => {
      const canvas = canvasRef.current;
      if (!canvas) throw new Error("Nothing to save");
      return new Promise<IncidentAttachmentDto>((resolve, reject) => {
        canvas.toBlob((blob) => {
          if (!blob) { reject(new Error("Couldn't render the annotated image")); return; }
          const file = new File([blob], `map-annotation-${incidentReference}-${Date.now()}.png`, { type: "image/png" });
          const body = new FormData();
          body.append("file", file);
          apiFetch<IncidentAttachmentDto>(`/incidents/${incidentId}/attachments`, { method: "POST", body })
            .then(resolve).catch(reject);
        }, "image/png");
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incident", incidentId] });
      showToast("Saved to Photos & Documents");
      onOpenChange(false);
      reset();
    },
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <Dialog.Root open={open} onOpenChange={(next) => { onOpenChange(next); if (!next) reset(); }}>
      <Dialog.Portal>
        {/* Same fix as CloseIncidentModal -- Leaflet's panes/controls carry
            inline z-index up to 1000, so this full-screen dialog needs to
            explicitly outrank them rather than relying on DOM order. */}
        <Dialog.Overlay className="fixed inset-0 z-[1100] bg-black/60" />
        <Dialog.Content className="fixed inset-4 z-[1100] flex flex-col rounded-card bg-(--surface) p-4 shadow-card">
          <div className="flex items-center justify-between">
            <Dialog.Title className="text-card-title font-semibold text-(--content-primary)">
              {stage === "view" ? "Annotate map" : "Draw on captured view"}
            </Dialog.Title>
            <Dialog.Close asChild>
              <button type="button" className="rounded-lg px-3 py-1.5 text-body text-(--content-secondary)">Close</button>
            </Dialog.Close>
          </div>

          {stage === "view" && (
            <div className="mt-3 flex flex-1 flex-col gap-3">
              {/* No routePoints -- a route from one particular appliance is
                  transient routing info, not something worth baking into a
                  shared, saved incident photo. */}
              <div ref={mapContainerRef} className="flex-1">
                <IncidentMap
                  latitude={latitude}
                  longitude={longitude}
                  label={label}
                  appliances={appliances}
                  geofenceRadiusMeters={geofenceRadiusMeters}
                  interactive
                  heightClassName="h-full"
                />
              </div>
              <div className="flex justify-end">
                <button
                  type="button"
                  disabled={capturing}
                  onClick={capture}
                  className="rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white disabled:opacity-60"
                >
                  {capturing ? "Capturing..." : "Capture this view"}
                </button>
              </div>
            </div>
          )}

          {stage === "annotate" && (
            <div className="mt-3 flex flex-1 flex-col gap-3">
              <div className="flex flex-1 items-center justify-center overflow-auto rounded-lg bg-(--surface-page)">
                <canvas
                  ref={canvasRef}
                  className="max-h-full max-w-full touch-none"
                  onPointerDown={onPointerDown}
                  onPointerMove={onPointerMove}
                  onPointerUp={onPointerUp}
                />
              </div>
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-3">
                  <div className="flex items-center gap-1.5">
                    {PEN_COLORS.map((c) => (
                      <button
                        key={c}
                        type="button"
                        title={PEN_COLOR_NAMES[c]}
                        aria-label={`${PEN_COLOR_NAMES[c]} pen`}
                        aria-pressed={color === c}
                        onClick={() => setColor(c)}
                        className={`h-6 w-6 rounded-full border-2 ${color === c ? "border-(--content-primary)" : "border-transparent"}`}
                        style={{ backgroundColor: c }}
                      />
                    ))}
                  </div>
                  <div className="flex items-center gap-1.5">
                    {PEN_WIDTHS.map((w) => (
                      <button
                        key={w}
                        type="button"
                        aria-label={`${w === PEN_WIDTHS[0] ? "Thin" : "Thick"} pen`}
                        aria-pressed={width === w}
                        onClick={() => setWidth(w)}
                        className={`flex h-7 w-7 items-center justify-center rounded-lg border ${width === w ? "border-brand-primary" : "border-(--surface-border)"}`}
                      >
                        <span className="rounded-full bg-(--content-primary)" style={{ width: w, height: w }} />
                      </button>
                    ))}
                  </div>
                  <button type="button" disabled={strokes.length === 0} onClick={undo} className="text-caption text-(--content-secondary) disabled:opacity-40">
                    Undo
                  </button>
                  <button type="button" disabled={strokes.length === 0} onClick={clearAnnotations} className="text-caption text-(--content-secondary) disabled:opacity-40">
                    Clear
                  </button>
                </div>
                <div className="flex items-center gap-2">
                  <button type="button" onClick={reset} className="rounded-lg border border-(--surface-border) px-3 py-1.5 text-body text-(--content-primary)">
                    Back to map
                  </button>
                  <button type="button" onClick={download} className="rounded-lg border border-(--surface-border) px-3 py-1.5 text-body text-(--content-primary)">
                    Download
                  </button>
                  <button
                    type="button"
                    disabled={saveMutation.isPending}
                    onClick={() => saveMutation.mutate()}
                    className="rounded-lg bg-brand-primary px-3 py-1.5 text-body font-semibold text-white disabled:opacity-60"
                  >
                    {saveMutation.isPending ? "Saving..." : "Save to incident"}
                  </button>
                </div>
              </div>
            </div>
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
