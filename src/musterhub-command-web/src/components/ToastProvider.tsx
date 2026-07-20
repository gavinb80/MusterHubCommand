import { createContext, useCallback, useContext, useState } from "react";
import * as Toast from "@radix-ui/react-toast";

interface ToastMessage {
  id: number;
  title: string;
  variant: "success" | "error";
}

interface ToastContextValue {
  showToast: (title: string, variant?: "success" | "error") => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error("useToast must be used within a ToastProvider");
  return ctx;
}

let nextId = 0;

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [messages, setMessages] = useState<ToastMessage[]>([]);

  const showToast = useCallback((title: string, variant: "success" | "error" = "success") => {
    setMessages((prev) => [...prev, { id: nextId++, title, variant }]);
  }, []);

  const dismiss = useCallback((id: number) => {
    setMessages((prev) => prev.filter((m) => m.id !== id));
  }, []);

  return (
    <ToastContext.Provider value={{ showToast }}>
      <Toast.Provider swipeDirection="right" duration={5000}>
        {children}
        {messages.map((m) => (
          <Toast.Root
            key={m.id}
            className={`rounded-card border p-3 shadow-card ${
              m.variant === "error"
                ? "border-red-300 bg-red-50 text-red-700"
                : "border-(--surface-border) bg-(--surface) text-(--content-primary)"
            }`}
            onOpenChange={(open: boolean) => {
              if (!open) dismiss(m.id);
            }}
          >
            <Toast.Title className="text-body font-semibold">{m.title}</Toast.Title>
          </Toast.Root>
        ))}
        <Toast.Viewport className="fixed bottom-4 right-4 z-50 flex w-96 max-w-[calc(100vw-2rem)] flex-col gap-2 outline-none" />
      </Toast.Provider>
    </ToastContext.Provider>
  );
}
