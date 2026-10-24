import { AppShell } from "@/components/app-shell";
import { requireAdmin } from "@/lib/server-session";

export default async function WorkspaceLayout({ children }: { children: React.ReactNode }) {
  await requireAdmin();
  return <AppShell>{children}</AppShell>;
}
