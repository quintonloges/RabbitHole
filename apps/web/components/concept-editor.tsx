"use client";
import { useState } from "react";
import { api, json } from "@/lib/api";
import type { Item } from "@/lib/types";
import { Notice } from "@/components/ui";

export function ConceptEditor({ item, onSaved }: { item: Item; onSaved: () => Promise<void> }) {
  const [editing, setEditing] = useState(false);
  const [tags, setTags] = useState("");
  const [excluded, setExcluded] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function save() {
    setBusy(true); setError(null);
    try {
      const conceptTags = tags.split(",").map(t => t.trim()).filter(Boolean);
      await api(`/items/${item.id}`, json("PATCH", { conceptTags, excludedConcepts: excluded }));
      await onSaved(); setEditing(false);
    } catch (e) { setError(e instanceof Error ? e.message : "Could not save concepts."); }
    finally { setBusy(false); }
  }
  const choices = [...(item.concepts || []).filter(c => c.field !== "user-tag").map(c => ({ id: c.id, label: c.label })),
    ...(item.excludedConcepts || []).filter(id => !item.concepts?.some(c => c.id === id)).map(id => ({ id, label: id.replaceAll("-", " ") }))];
  return <details className="details" style={{ marginTop: 16 }}><summary>Concepts & tags</summary>
    <p className="subtle">Tags help find related items. Your interests are set separately in Settings.</p>
    <div className="chip-row">{item.concepts?.map(c => <span className="pill" key={c.id}>{c.label}{c.field === "user-tag" ? " · your tag" : ""}</span>)}</div>
    {!item.concepts?.length && <p className="subtle">No concepts found in the text. You can add your own tags.</p>}
    <Notice error={error}/>
    {!editing ? <button className="btn small" style={{ marginTop: 12 }} onClick={() => { setTags((item.conceptTags || []).join(", ")); setExcluded(item.excludedConcepts || []); setEditing(true); }}>Edit concepts</button>
      : <div className="stack" style={{ marginTop: 12 }}>
        {choices.length > 0 && <div><span className="subtle">Include these concepts</span><div className="chip-row">{choices.map(c => <label className="pill" key={c.id}><input type="checkbox" checked={!excluded.includes(c.id)} onChange={e => setExcluded(e.target.checked ? excluded.filter(id => id !== c.id) : [...excluded, c.id])}/>{c.label}</label>)}</div></div>}
        <label className="field">Your tags<input value={tags} onChange={e => setTags(e.target.value)} placeholder="Global Workspace Theory, digital filters"/><small>Separate tags with commas. Matching tags help find related items.</small></label>
        <div className="action-row"><button className="btn primary" disabled={busy} onClick={() => void save()}>Save concepts</button><button className="btn" disabled={busy} onClick={() => setEditing(false)}>Cancel</button></div>
      </div>}
  </details>;
}
