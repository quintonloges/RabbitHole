"use client";
import { useEffect, useState } from "react";
import { api, json, resetCsrf } from "@/lib/api";

export function LogoutButton() {
  const [ready, setReady] = useState(false);
  useEffect(() => { setReady(true); }, []);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function logout() {
    setBusy(true); setError(null);
    try { await api("/auth/logout", json("POST")); resetCsrf(); sessionStorage.clear(); window.location.replace("/admin/login"); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Could not sign out."); setBusy(false); }
  }
  return <div><button className="btn small" onClick={logout} disabled={!ready || busy}>{busy ? "Signing out…" : "Sign out"}</button>{error && <p role="alert" className="error">{error}</p>}</div>;
}

export function PasswordSettings() {
  const [currentPassword, setCurrent] = useState(""); const [newPassword, setNew] = useState("");
  const [confirm, setConfirm] = useState(""); const [error, setError] = useState<string | null>(null); const [busy, setBusy] = useState(false);
  async function change(event: React.FormEvent) {
    event.preventDefault(); setError(null);
    if (newPassword !== confirm) { setError("The new passwords do not match."); return; }
    setBusy(true);
    try { await api("/auth/password", json("POST", { currentPassword, newPassword })); resetCsrf(); sessionStorage.clear(); window.location.replace("/admin/login"); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Could not change password."); setBusy(false); }
  }
  return <section className="panel panel-pad"><h2 className="section-title">Administrator password</h2><p className="muted">Changing your password signs out all sessions. Sign in again with the new password.</p><form onSubmit={change} className="stack"><div className="form-grid"><label className="field">Current password<input type="password" required autoComplete="current-password" value={currentPassword} onChange={e => setCurrent(e.target.value)}/></label><label className="field">New password<input type="password" minLength={12} required autoComplete="new-password" value={newPassword} onChange={e => setNew(e.target.value)}/></label><label className="field">Confirm new password<input type="password" required autoComplete="new-password" value={confirm} onChange={e => setConfirm(e.target.value)}/></label></div>{error && <p role="alert" className="error">{error}</p>}<div><button className="btn" disabled={busy}>{busy ? "Changing…" : "Change password"}</button></div></form></section>;
}
