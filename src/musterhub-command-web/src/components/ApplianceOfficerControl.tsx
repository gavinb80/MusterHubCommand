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
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const mutation = useMutation({
    mutationFn: (body: SetApplianceOfficerRequest) =>
      apiFetch<IncidentDto>(`/incidents/${incidentId}/appliances/${appliance.id}/officer`, { method: "PATCH", body: JSON.stringify(body) }),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["incident", incidentId] }); setEditing(false); },
    onError: (error) => showToast(error.message, "error"),
  });

  if (editing) {
    return (
      <div className="mt-1 w-48">
        <PersonPicker
          employees={employees}
          employeeId={appliance.officerInChargeEmployeeId}
          name={appliance.officerInChargeName}
          placeholder="Officer in charge..."
          onChange={(employeeId, name) => mutation.mutate({ officerInChargeEmployeeId: employeeId, officerInChargeName: name })}
        />
      </div>
    );
  }

  return (
    <button type="button" onClick={() => setEditing(true)} className="text-caption text-(--content-secondary)">
      {appliance.officerInChargeName ? `Officer: ${appliance.officerInChargeName}` : "+ Assign officer"}
    </button>
  );
}
