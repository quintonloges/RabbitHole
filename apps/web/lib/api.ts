export class ApiError extends Error {
  constructor(message: string, public status: number) { super(message); }
}

let csrfToken: Promise<string> | null = null;
export function resetCsrf() { csrfToken = null; }
async function csrf(): Promise<string> {
  csrfToken ??= fetch("/api/auth/csrf", { cache: "no-store", credentials: "same-origin" }).then(async response => {
    if (!response.ok) throw new ApiError("Could not prepare this request. Please try again.", response.status);
    const data = await response.json() as { token: string };
    if (!data.token) throw new ApiError("The service did not provide a request token.", 0);
    return data.token;
  }).catch(cause => { resetCsrf(); throw cause; });
  return csrfToken;
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const isForm = options.body instanceof FormData;
  let response: Response;
  const headers = new Headers(options.headers);
  if (!isForm && options.body) headers.set("Content-Type", "application/json");
  if (!["GET", "HEAD", "OPTIONS"].includes((options.method || "GET").toUpperCase())) headers.set("X-CSRF-TOKEN", await csrf());
  try {
    response = await fetch(`/api${path}`, {
      ...options,
      cache: "no-store", credentials: "same-origin", headers,
    });
  } catch {
    throw new ApiError("The RabbitHole service is unavailable. Please try again in a moment.", 0);
  }
  if (!response.ok) {
    if (response.status === 400 || response.status === 403) resetCsrf();
    if (response.status === 401 && !path.startsWith("/auth/")) {
      resetCsrf();
      if (typeof window !== "undefined") window.location.replace("/admin/login");
    }
    const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string; error?: string };
    throw new ApiError(problem.detail || problem.title || problem.error || `Request failed (${response.status})`, response.status);
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
