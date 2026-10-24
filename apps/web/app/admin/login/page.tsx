"use client";
import Link from "next/link";
import { useState } from "react";
import { api, json, resetCsrf } from "@/lib/api";

export default function LoginPage() {
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function login(event: React.FormEvent) {
    event.preventDefault(); setBusy(true); setError(null);
    try {
      resetCsrf();
      await api("/auth/login", json("POST", { password }));
      resetCsrf(); setPassword(""); window.location.replace("/admin");
    } catch (cause) { resetCsrf(); setError(cause instanceof Error ? cause.message : "Could not sign in."); setBusy(false); }
  }
  return <main className="login-page"><Link className="brand" href="/">↘ RabbitHole</Link><section className="panel panel-pad"><h1>Private workspace</h1><p className="muted">Sign in to manage your library and follow your own rabbit holes.</p><form onSubmit={login} className="stack"><label className="field">Administrator password<input type="password" autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} required autoFocus/></label>{error && <p className="error" role="alert">{error}</p>}<button className="btn primary" disabled={busy || !password}>{busy ? "Signing in…" : "Sign in"}</button></form><p className="subtle">Password recovery is available through the server’s administrator command.</p><Link className="text-link" href="/">Back to the public catalog →</Link></section></main>;
}
