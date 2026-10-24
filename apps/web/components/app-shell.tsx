"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { BookOpen, Compass, Network, Search, Settings2, Sparkles, ArrowUpRight } from "lucide-react";
import { useEffect } from "react";
import { api } from "@/lib/api";
import type { Settings } from "@/lib/types";
import { LogoutButton } from "./account-controls";

const links = [
  { href: "/admin", label: "Discover", icon: Compass },
  { href: "/admin/holes", label: "Rabbit Holes", icon: Network },
  { href: "/admin/library", label: "Library", icon: BookOpen },
  { href: "/admin/practice", label: "Practice", icon: Sparkles },
  { href: "/admin/search", label: "Search", icon: Search },
  { href: "/admin/settings", label: "Settings", icon: Settings2 },
];

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  useEffect(() => { api<Settings>("/settings").then(settings => { document.documentElement.dataset.theme = settings.theme; localStorage.setItem("rabbithole-theme", settings.theme); }).catch(() => {}); }, []);
  return <div className="app-shell">
    <aside className="sidebar" aria-label="Main navigation"><Link href="/admin" className="brand"><span className="brand-icon">↘</span><span>Rabbit<span className="brand-light">Hole</span></span></Link>
      <nav className="nav-list">{links.map(({ href, label, icon: Icon }) => <Link key={href} href={href} className={`nav-link ${pathname === href || href !== "/admin" && pathname.startsWith(href + "/") ? "active" : ""}`} aria-current={pathname === href ? "page" : undefined}><Icon size={18} strokeWidth={1.8}/><span>{label}</span>{href === "/admin" && <span className="nav-dot"/>}</Link>)}</nav>
    </aside>
    <div className="page-column"><header className="topbar"><Link href="/admin" className="mobile-brand">↘ RabbitHole</Link><Link href="/" className="subtle">Public catalog</Link><div className="action-row" style={{ marginLeft: "auto" }}><Link href="/admin/search" className="topbar-search" aria-label="Search"><Search size={16}/> <span>Search</span><ArrowUpRight size={14}/></Link><LogoutButton/></div></header><main id="main-content">{children}</main><footer className="page-footer"><span>RabbitHole · Private workspace</span></footer></div>
    <nav className="mobile-nav" aria-label="Mobile navigation">{links.map(({ href, label, icon: Icon }) => <Link key={href} href={href} className={pathname === href || href !== "/admin" && pathname.startsWith(href + "/") ? "active" : ""} aria-label={label}><Icon size={20}/><span>{label === "Rabbit Holes" ? "Holes" : label}</span></Link>)}</nav>
  </div>;
}
