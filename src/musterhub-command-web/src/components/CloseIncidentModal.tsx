import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as Dialog from "@radix-ui/react-dialog";
import { apiFetch } from "../auth/apiClient";
import { useToast } from "./ToastProvider";
import type { IncidentCloseTypeDto, IncidentDto } from "../api/types";

export function CloseIncidentModal({ incidentId }: { incidentId: string }) {
  const [open, setOpen] = useState(false);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const closeTypesQuery = useQuery({
    queryKey: ["incident-close-types"],
    queryFn: () => apiFetch<IncidentCloseTypeDto[]>("/incident-close-types"),
    enabled: open,
  });

  const closeMutation = useMutation({
    mutationFn: (request: { closeTypeId: string | null; actionsTaken: string | null; outcome: string | null }) =>
      apiFetch<IncidentDto>(`/incidents/${incidentId}/close`, { method: "POST", body: JSON.stringify(request) }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["incident", incidentId] });
      queryClient.invalidateQueries({ queryKey: ["incidents"] });
      showToast("Incident closed");
      setOpen(false);
    },
    onError: (error) => showToast(error.message, "error"),
  });

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger asChild>
        <button type="button" className="rounded-lg border border-(--surface-border) px-3 py-1.5 text-body text-(--content-primary)">
          Close
        </button>
      </Dialog.Trigger>
      <Dialog.Portal>
        {/* Leaflet's own panes/controls carry inline z-index up to 1000, and
            .leaflet-container's position:relative alone doesn't contain
            them into their own stacking context -- an unset (auto) z-index
            here lets the map paint over the dialog on any incident that has
            a location set. */}
        <Dialog.Overlay className="fixed inset-0 z-[1100] bg-black/40" />
        <Dialog.Content className="fixed left-1/2 top-1/2 z-[1100] w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-card bg-(--surface) p-4 shadow-card">
          <Dialog.Title className="text-card-title font-semibold text-(--content-primary)">
            Close incident
          </Dialog.Title>
          <p className="mt-1 text-body text-(--content-secondary)">
            A close-out summary is optional but helps whoever reads the exported report later.
          </p>
          <form
            className="mt-4 flex flex-col gap-3"
            onSubmit={(e) => {
              e.preventDefault();
              const form = new FormData(e.currentTarget);
              const closeTypeId = String(form.get("closeTypeId") || "");
              closeMutation.mutate({
                closeTypeId: closeTypeId || null,
                actionsTaken: String(form.get("actionsTaken") || "") || null,
                outcome: String(form.get("outcome") || "") || null,
              });
            }}
          >
            <label className="flex flex-col gap-1 text-body text-(--content-primary)">
              Close type
              <select name="closeTypeId" className="rounded-lg border border-(--surface-border) px-3 py-2">
                <option value="">Not classified</option>
                {closeTypesQuery.data?.map((t) => (
                  <option key={t.id} value={t.id}>{t.code} - {t.name}</option>
                ))}
              </select>
            </label>
            <label className="flex flex-col gap-1 text-body text-(--content-primary)">
              Actions taken
              <textarea name="actionsTaken" className="rounded-lg border border-(--surface-border) px-3 py-2" rows={3} />
            </label>
            <label className="flex flex-col gap-1 text-body text-(--content-primary)">
              Outcome
              <textarea name="outcome" className="rounded-lg border border-(--surface-border) px-3 py-2" rows={2} />
            </label>
            <div className="mt-2 flex justify-end gap-2">
              <Dialog.Close asChild>
                <button type="button" className="rounded-lg px-4 py-2 text-body text-(--content-secondary)">Cancel</button>
              </Dialog.Close>
              <button
                type="submit"
                disabled={closeMutation.isPending}
                className="rounded-lg bg-brand-primary px-4 py-2 text-body font-semibold text-white disabled:opacity-60"
              >
                {closeMutation.isPending ? "Closing..." : "Close incident"}
              </button>
            </div>
          </form>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
