import { useState } from "react";
import type { EmployeeDto } from "../api/types";

// Free text until it matches a real employee -- same dual-mode rule the
// API itself enforces (an employee link always wins over free text, see
// IncidentService.UpdateSectorAsync). Filters client-side since the org's
// employee list is already fetched whole wherever this is used.
export function PersonPicker({ employees, employeeId, name, onChange, placeholder = "Person in charge..." }: {
  employees: EmployeeDto[];
  employeeId: string | null;
  name: string | null;
  onChange: (employeeId: string | null, name: string | null) => void;
  placeholder?: string;
}) {
  const [query, setQuery] = useState(name ?? "");
  const [open, setOpen] = useState(false);
  const matches = query.trim()
    ? employees.filter((e) => e.displayName.toLowerCase().includes(query.trim().toLowerCase())).slice(0, 6)
    : [];

  return (
    <div className="relative">
      <input
        value={query}
        onChange={(e) => {
          setQuery(e.target.value);
          setOpen(true);
          // Typing invalidates any previously-picked employee link until
          // they pick again from the list -- otherwise editing the text
          // after a pick would silently keep pointing at the old employee.
          onChange(null, e.target.value.trim() || null);
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        placeholder={placeholder}
        className="w-full rounded-lg border border-(--surface-border) px-2 py-1 text-caption"
      />
      {open && matches.length > 0 && (
        <ul className="absolute z-10 mt-1 w-full rounded-lg border border-(--surface-border) bg-(--surface) shadow-card">
          {matches.map((e) => (
            <li key={e.id}>
              <button
                type="button"
                onMouseDown={(ev) => ev.preventDefault()}
                onClick={() => { setQuery(e.displayName); onChange(e.id, e.displayName); setOpen(false); }}
                className="block w-full px-2 py-1 text-left text-caption text-(--content-primary) hover:bg-(--row-alt,theme(colors.gray.100))"
              >
                {e.displayName}
              </button>
            </li>
          ))}
        </ul>
      )}
      {employeeId && <p className="mt-0.5 text-caption text-(--content-secondary)">Linked to {name}</p>}
    </div>
  );
}
