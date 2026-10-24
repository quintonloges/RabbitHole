import { PublicShell } from "@/components/public-shell";
export const dynamic = "force-dynamic";
export default function PublicLayout({ children }: { children: React.ReactNode }) { return <PublicShell>{children}</PublicShell>; }
