"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const links = [
  { href: "/", label: "Catalog" },
  { href: "/search", label: "Search" },
  { href: "/blackjack", label: "Blackjack Lab" },
];

export function PublicNavigation() {
  const pathname = usePathname();
  return <nav className="public-navigation" aria-label="Public navigation">
    {links.map(({ href, label }) => <Link prefetch={false} key={href} href={href} aria-current={pathname === href ? "page" : undefined}>{label}</Link>)}
  </nav>;
}
