import Link from "next/link";
import { ArrowDownRight } from "lucide-react";
import { PublicNavigation } from "./public-navigation";
import "./public-shell.css";

export function PublicShell({ children }: { children: React.ReactNode }) {
  return <div className="public-shell">
    <a className="public-skip-link" href="#main-content">Skip to content</a>
    <header className="public-masthead">
      <div className="public-masthead-inner">
        <Link prefetch={false} href="/" className="public-brand" aria-label="RabbitHole home">
          <span className="public-brand-mark"><ArrowDownRight size={20} strokeWidth={1.8} aria-hidden="true"/></span>
          <span>Rabbit<span className="brand-light">Hole</span></span>
        </Link>
        <PublicNavigation/>
        <a href="/admin" className="public-workspace">Private workspace <span aria-hidden="true">↗</span></a>
      </div>
    </header>
    <main id="main-content" tabIndex={-1}>{children}</main>
    <footer className="public-footer">
      <p>Start anywhere. See where curiosity leads.</p>
      <a href="/admin">Private workspace <span aria-hidden="true">↗</span></a>
    </footer>
  </div>;
}
