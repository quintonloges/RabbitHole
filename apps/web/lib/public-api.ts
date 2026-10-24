import "server-only";
import { headers } from "next/headers";
import { isIP } from "node:net";

export const publicOrigin = () => new URL(process.env.PUBLIC_ORIGIN || "http://127.0.0.1:47832").origin;
export const indexingEnabled = () => process.env.PUBLIC_INDEXING_ENABLED === "true";
export class PublicApiError extends Error { constructor(public status: number) { super("The public catalog is temporarily unavailable."); } }

export async function publicApi<T>(path: string): Promise<T> {
  const proxyHeaders: Record<string, string> = {};
  // Hosted Caddy overwrites this header, and Next is reachable only on its private network.
  // Accept a single literal IP, never an arbitrary forwarded chain or any auth credentials.
  if (process.env.TRUST_PROXY_HEADERS === "true") {
    const address = (await headers()).get("x-forwarded-for")?.trim();
    if (address && isIP(address)) {
      proxyHeaders["X-Forwarded-For"] = address;
      proxyHeaders["X-Forwarded-Proto"] = "https";
      proxyHeaders["X-Forwarded-Host"] = new URL(publicOrigin()).host;
    }
  }
  // Never forward cookies, authorization, or other browser request headers.
  const response = await fetch(`${process.env.API_ORIGIN || "http://127.0.0.1:5080"}/api/public${path}`, {
    headers: proxyHeaders, cache: "no-store", credentials: "omit", redirect: "error", signal: AbortSignal.timeout(15000),
  });
  if (!response.ok) throw new PublicApiError(response.status);
  return response.json() as Promise<T>;
}
