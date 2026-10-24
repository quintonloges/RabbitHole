"use client";
import { useState } from "react";
import { api, json } from "@/lib/api";
import type { Item } from "@/lib/types";

export function PublicationControl({ item, onChange }: { item: Item; onChange?: (item: Item) => void }) {
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string | null>(null);
  const [pendingVisibility, setPendingVisibility] = useState<boolean | null>(null);
  async function change(isPublic: boolean) {
    setBusy(true); setPendingVisibility(isPublic); setError(null);
    try { await api(`/items/${item.id}/publication`, json("PATCH", { isPublic })); const updated = await api<Item>(`/items/${item.id}`); onChange?.(updated); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Could not change publication."); }
    finally { setPendingVisibility(null); setBusy(false); }
  }
  return <div className="publication-control"><label className="field" style={{ display: "flex", alignItems: "center", gap: 8 }}><input type="checkbox" checked={pendingVisibility ?? item.isPublic} disabled={busy || !item.canPublish} onChange={event => void change(event.target.checked)}/>Show publicly{busy && <span className="subtle">Saving…</span>}</label>{!item.canPublish && <p className="subtle">This item is private. Uploaded documents and ineligible source links cannot be published.</p>}{error && <p className="error" role="alert">{error}</p>}</div>;
}
