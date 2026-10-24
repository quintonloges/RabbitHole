"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { BookOpen, Compass, Network, Search, Settings2, Sparkles, ArrowUpRight } from "lucide-react";
import { useEffect } from "react";
import { api } from "@/lib/api";
import type { Settings } from "@/lib/types";

const links = [
  { href: "/", label: "Discover", icon: Compass },
  { href: "/holes", label: "Rabbit Holes", icon: Network },
  { href: "/library", label: "Library", icon: BookOpen },
  { href: "/practice", label: "Practice", icon: Sparkles },
  { href: "/search", label: "Search", icon: Search },
  { href: "/settings", label: "Settings", icon: Settings2 },
];

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  useEffect(() => { api<Settings>("/settings").then(settings => { document.documentElement.dataset.theme = settings.theme; localStorage.setItem("rabbithole-theme", settings.theme); }).catch(() => {}); }, []);
  return <div className="app-shell">
    <aside className="sidebar" aria-label="Main navigation"><Link href="/" className="brand"><span className="brand-icon">↘</span><span>Rabbit<span className="brand-light">Hole</span></span></Link>
      <nav className="nav-list">{links.map(({ href, label, icon: Icon }) => <Link key={href} href={href} className={`nav-link ${pathname === href || href !== "/" && pathname.startsWith(href + "/") ? "active" : ""}`} aria-current={pathname === href ? "page" : undefined}><Icon size={18} strokeWidth={1.8}/><span>{label}</span>{href === "/" && <span className="nav-dot"/>}</Link>)}</nav>
    </aside>
    <div className="page-column"><header className="topbar"><Link href="/" className="mobile-brand">↘ RabbitHole</Link><Link href="/search" className="topbar-search" style={{ marginLeft: "auto" }} aria-label="Search"><Search size={16}/> <span>Search</span><ArrowUpRight size={14}/></Link></header><main id="main-content">{children}</main><footer className="page-footer"><span>RabbitHole</span></footer></div>
    <nav className="mobile-nav" aria-label="Mobile navigation">{links.map(({ href, label, icon: Icon }) => <Link key={href} href={href} className={pathname === href || href !== "/" && pathname.startsWith(href + "/") ? "active" : ""} aria-label={label}><Icon size={20}/><span>{label === "Rabbit Holes" ? "Holes" : label}</span></Link>)}</nav>
  </div>;
}
