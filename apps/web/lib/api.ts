export class ApiError extends Error {
  constructor(message: string, public status: number) { super(message); }
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const isForm = options.body instanceof FormData;
  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      cache: "no-store",
      ...options,
      headers: { ...(isForm ? {} : options.body ? { "Content-Type": "application/json" } : {}), ...options.headers },
    });
  } catch {
    throw new ApiError("The local service is unavailable. Check that RabbitHole is running.", 0);
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string };
    throw new ApiError(problem.detail || problem.title || `Request failed (${response.status})`, response.status);
  }
  if (response.status === 204) return undefined as T;
  const type = response.headers.get("content-type") || "";
  if (!type.includes("json")) return undefined as T;
  return response.json() as Promise<T>;
}

export const json = (method: string, data?: unknown): RequestInit => ({ method, body: data === undefined ? undefined : JSON.stringify(data) });
export const query = (values: Record<string, string | number | boolean | undefined | null>) => {
  const params = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => { if (value !== undefined && value !== null && value !== "") params.set(key, String(value)); });
  return params.toString();
};
export function readableDate(value?: string | null) {
  if (!value) return "Date unknown";
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? "Date unknown" : date.toLocaleDateString(undefined, { month: "long", day: "numeric", year: "numeric", ...(/^\d{4}-\d{2}-\d{2}$/.test(value) ? { timeZone: "UTC" } : {}) });
}
// Publication dates (including date-only scholarly records) must not shift to the previous day in a reader's timezone.
export function publicationDate(value?: string | null) {
  if (!value) return "Date unknown";
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? "Date unknown" : date.toLocaleDateString(undefined, { month: "long", day: "numeric", year: "numeric", timeZone: "UTC" });
}
export const shortDate = (value?: string | null) => value ? readableDate(value) : "Date unknown";
export function safeExternal(url?: string | null) {
  try { const parsed = new URL(url || ""); return ["https:", "http:"].includes(parsed.protocol) ? parsed.href : undefined; } catch { return undefined; }
}
