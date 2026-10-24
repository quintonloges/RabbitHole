import { cookies } from "next/headers";
import { redirect } from "next/navigation";

// This is the only server-rendered request that forwards browser credentials.
export async function requireAdmin() {
  const cookie = (await cookies()).toString();
  const response = await fetch(`${process.env.API_ORIGIN || "http://127.0.0.1:5080"}/api/auth/session`, {
    headers: { Cookie: cookie }, cache: "no-store", redirect: "error", signal: AbortSignal.timeout(10000),
  });
  if (response.status === 401 || response.status === 403) redirect("/admin/login");
  if (!response.ok) throw new Error("The administrator session could not be checked. Please try again.");
  const session = await response.json() as { authenticated?: boolean };
  if (!session.authenticated) redirect("/admin/login");
}
