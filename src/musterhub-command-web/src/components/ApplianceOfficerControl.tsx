import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch } from "../auth/apiClient";
import { useToast } from "./ToastProvider";
import { PersonPicker } from "./PersonPicker";
import type { EmployeeDto, IncidentApplianceDto, IncidentDto, SetApplianceOfficerRequest } from "../api/types";

// Small inline officer-assignment control for one appliance -- opens on
// click, same PersonPicker the sector editor uses, closes once a value is
// committed. Shared between the flat Attendance card and the hierarchy
// tree's appliance chips, since an appliance's officer is the same
// underlying field in both places.
export function ApplianceOfficerControl({ incidentId, appliance, employees }: {
  incidentId: string;
  appliance: IncidentApplianceDto;
  employees: EmployeeDto[];
}) {
  const [editing, setEditing] = useState(false);
  const [employeeId, setEmployeeId] = useState<string | null>(appliance.officerInChargeEmployeeId);
  const [name, setName] = useState<string | null>(appliance.officerInChargeName);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const mutation = useMutation({
    mutationFn: (body: SetApplianceOfficerRequest) =>
      apiFetch<IncidentDto>(`/incidents/${incidentId}/appliances/${appliance.id}/officer`, { method: "PATCH", body: JSON.stringify(body) }),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["incident", incidentId] }); setEditing(false); },
    onError: (error) => showToast(error.message, "error"),
  });

  const startEditing = () => {
    setEmployeeId(appliance.officerInChargeEmployeeId);
    setName(appliance.officerInChargeName);
    setEditing(true);
  };

  if (editing) {
    return (
      <div className="mt-1 flex w-48 flex-col gap-1">
        <PersonPicker
          employees={employees}
          employeeId={employeeId}
          name={name}
          placeholder="Officer in charge..."
          onChange={(newEmployeeId, newName) => { setEmployeeId(newEmployeeId); setName(newName); }}
        />
        <div className="flex justify-end gap-2">
          <button type="button" onClick={() => setEditing(false)} className="text-caption text-(--content-secondary)">
            Cancel
          </button>
          <button
            type="button"
            disabled={mutation.isPending}
            onClick={() => mutation.mutate({ officerInChargeEmployeeId: employeeId, officerInChargeName: name })}
            className="text-caption font-semibold text-brand-primary disabled:opacity-60"
          >
            Save
          </button>
        </div>
      </div>
    );
  }

  return (
    <button type="button" onClick={startEditing} className="text-caption text-(--content-secondary)">
      {appliance.officerInChargeName ? `Officer: ${appliance.officerInChargeName}` : "+ Assign officer"}
    </button>
  );
}
