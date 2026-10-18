"use client";
import Link from "next/link";
import { useRef, useState } from "react";
import { ArrowRight, Brain, Clock3, Sparkles } from "lucide-react";
import { api, json, readableDate } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { Item, Prompt, Settings } from "@/lib/types";
import { Empty, Loading, Notice } from "@/components/ui";

export default function PracticePage() {
  const due = useApi<Prompt[]>("/practice/due");
  const all = useApi<Prompt[]>("/practice/prompts");
  const items = useApi<{ items: Item[] }>("/items?limit=100");
  const [itemSearch, setItemSearch] = useState("");
  const searchResults = useApi<{ items: Item[] }>(itemSearch.trim().length >= 2 ? `/search?q=${encodeURIComponent(itemSearch.trim())}&limit=100` : null);
  const settings = useApi<Settings>("/settings");
  const ai = useApi<{ configured?: boolean; enabled?: boolean }>("/ai/status");
  const [active, setActive] = useState<string | null>(null);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [createdFor, setCreatedFor] = useState("");
  const [kind, setKind] = useState<"reflection" | "recall" | "compare" | "reasoning" | "architecture">("reflection");
  const [critiques, setCritiques] = useState<Record<string, { text: string; evidenceItemIds: string[]; error?: string }>>({});
  const [aiBusy, setAiBusy] = useState<Record<string, boolean>>({});
  const critiqueVersions = useRef<Record<string, number>>({});
  function updateDraft(id: string, value: string) {
    setDrafts(old => ({ ...old, [id]: value }));
    critiqueVersions.current[id] = (critiqueVersions.current[id] || 0) + 1;
    setCritiques(old => { const next = { ...old }; delete next[id]; return next; });
  }
  async function attempt(prompt: Prompt, rating: string) {
    const draft = drafts[prompt.id] || "";
    setBusy(true); setError(null);
    try {
      await api(`/practice/prompts/${prompt.id}/attempts`, json("POST", { response: rating === "skip" ? "" : draft.trim(), rating }));
      if (rating !== "skip") {
        setDrafts(old => old[prompt.id] === draft ? { ...old, [prompt.id]: "" } : old);
        critiqueVersions.current[prompt.id] = (critiqueVersions.current[prompt.id] || 0) + 1;
        setCritiques(old => { const next = { ...old }; delete next[prompt.id]; return next; });
      }
      setActive(current => current === prompt.id ? null : current);
      await Promise.all([due.reload(), all.reload()]);
    } catch (e) { setError(e instanceof Error ? e.message : "Could not save reflection."); }
    finally { setBusy(false); }
  }
  async function requestCritique(prompt: Prompt) {
    const response = (drafts[prompt.id] || "").trim();
    if (!response || aiBusy[prompt.id]) return;
    const version = (critiqueVersions.current[prompt.id] || 0) + 1;
    critiqueVersions.current[prompt.id] = version;
    setAiBusy(old => ({ ...old, [prompt.id]: true }));
    try {
      const result = await api<{ text: string; evidenceItemIds: string[] }>(`/items/${prompt.itemId}/ai`, json("POST", { action: "critique", response }));
      if (critiqueVersions.current[prompt.id] === version) setCritiques(old => ({ ...old, [prompt.id]: result }));
    } catch (e) {
      if (critiqueVersions.current[prompt.id] === version) setCritiques(old => ({ ...old, [prompt.id]: { text: "", evidenceItemIds: [], error: e instanceof Error ? e.message : "AI critique unavailable." } }));
    } finally { setAiBusy(old => ({ ...old, [prompt.id]: false })); }
  }
  async function create() { if (!createdFor) return; setBusy(true); setError(null); try { await api("/practice/prompts", json("POST", { itemId: createdFor, kind })); setCreatedFor(""); setItemSearch(""); await Promise.all([due.reload(), all.reload()]); } catch (e) { setError(e instanceof Error ? e.message : "Could not create prompt."); } finally { setBusy(false); } }
  function activatePrompt(id: string) { setActive(id); setError(null); }
  async function enable() { setError(null); try { await api("/settings", json("PATCH", { practiceEnabled: true })); settings.setData(old => old ? { ...old, practiceEnabled: true } : old); } catch (e) { setError(e instanceof Error ? e.message : "Could not enable practice."); } }
  return <><div className="page-heading"><div><span className="eyebrow accent">✳ THE QUIET MEMORY</span><h1>Practice, if you like</h1><p>A gentle way to revisit ideas you chose. Write what you remember, rate how it felt, or say “not now.” Nothing here measures your worth.</p></div></div><Notice error={due.error || all.error || error || (active ? critiques[active]?.error : null)} onRetry={() => { void due.reload(); void all.reload(); }}/>{settings.data && !settings.data.practiceEnabled ? <Empty icon={<Brain size={22}/>} title="Practice is resting" body="Turn it on when you want occasional, source-linked reflection. Reading and exploring work exactly as before." action={<button className="btn primary" onClick={enable}>Enable practice</button>}/> : <div className="two-column"><div><h2 className="section-title">Ready when you are</h2>{due.loading && !due.data ? <Loading/> : !due.data?.length ? <Empty icon={<Sparkles size={22}/>} title="Nothing due right now" body="You’re free to wander. Reflections appear only for real items you choose, and you can make one below."/> : <div className="stack">{due.data.map(prompt => <div key={prompt.id} className="panel panel-pad"><span className="eyebrow">FROM {prompt.itemTitle || "YOUR READING"} · DUE {readableDate(prompt.dueAt)}</span><h3 className="serif" style={{ fontSize: 24, margin: "13px 0" }}>{prompt.question}</h3><details className="details"><summary>Reveal source passage</summary>{prompt.reference ? <blockquote className="quote-box" style={{ whiteSpace: "pre-wrap", margin: "12px 0" }}>{prompt.reference}</blockquote> : <p>No passage was available when this prompt was created. Open the original source to review it.</p>}<Link className="text-link" href={`/items/${prompt.itemId}`}>Open {prompt.itemTitle || "source"} →</Link></details>{prompt.attempts?.some(attempt => attempt.response.trim()) && <details className="details"><summary>Past answers ({prompt.attempts.filter(attempt => attempt.response.trim()).length})</summary>{prompt.attempts.filter(attempt => attempt.response.trim()).map(attempt => <div key={attempt.id} className="panel panel-pad" style={{ marginTop: 10 }}><p style={{ whiteSpace: "pre-wrap" }}>{attempt.response}</p><span className="subtle">{readableDate(attempt.createdAt)} · {attempt.rating}</span></div>)}</details>}{active === prompt.id ? <div className="stack"><label className="field">What comes to mind?<textarea value={drafts[prompt.id] || ""} onChange={e => updateDraft(prompt.id, e.target.value)} placeholder="Your own words are enough."/></label>{ai.data?.configured && ai.data?.enabled && <button className="btn small" disabled={aiBusy[prompt.id] || !(drafts[prompt.id] || "").trim()} onClick={() => void requestCritique(prompt)}><Sparkles size={13}/>Ask AI to compare with source</button>}{critiques[prompt.id]?.text && <div className="panel panel-pad"><span className="eyebrow">OPTIONAL AI CRITIQUE</span><p style={{ whiteSpace: "pre-wrap" }}>{critiques[prompt.id].text}</p>{critiques[prompt.id].evidenceItemIds.map((id, index) => <Link key={id} className="pill" href={`/items/${id}`}>Source {index + 1} →</Link>)}</div>}<span className="subtle">How did recalling this feel?</span><div className="action-row">{(["again", "hard", "good", "easy"] as const).map(rating => <button key={rating} className="btn small" disabled={busy} onClick={() => void attempt(prompt, rating)}>{rating[0].toUpperCase() + rating.slice(1)}</button>)}<button className="btn small ghost" disabled={busy} onClick={() => void attempt(prompt, "skip")}>Not now</button></div></div> : <div className="action-row"><button className="btn primary" onClick={() => activatePrompt(prompt.id)}>Reflect <ArrowRight size={14}/></button><button className="btn" disabled={busy} onClick={() => void attempt(prompt, "skip")}>Not now</button><Link className="btn ghost" href={`/items/${prompt.itemId}`}>Revisit source</Link></div>}</div>)}</div>}</div><aside><div className="panel panel-pad"><Clock3 size={26} color="var(--accent)"/><h2 className="section-title" style={{ marginTop: 12 }}>Choose a thread</h2><p className="muted" style={{ lineHeight: 1.6 }}>Choose a real source from your collection. A prompt will refer to the available passage.</p><label className="field">Find a source<input value={itemSearch} onChange={e => setItemSearch(e.target.value)} placeholder="Search the catalog by title or idea"/></label>{searchResults.loading && itemSearch.trim().length >= 2 && <span className="subtle" role="status">Searching sources…</span>}<label className="field">Source<select value={createdFor} onChange={e => setCreatedFor(e.target.value)}><option value="">Choose an item</option>{(itemSearch.trim().length >= 2 ? searchResults.data?.items : items.data?.items)?.map(i => <option key={i.id} value={i.id}>{i.title}</option>)}</select></label><label className="field" style={{ marginTop: 12 }}>Prompt kind<select value={kind} onChange={e => setKind(e.target.value as typeof kind)}><option value="reflection">Reflection</option><option value="recall">Recall</option><option value="compare">Compare</option><option value="reasoning">Reasoning</option><option value="architecture">Architecture</option></select></label><button className="btn" style={{ marginTop: 13 }} onClick={create} disabled={!createdFor || busy}>Create {kind}</button><hr className="rule"/><span className="eyebrow">LATER</span>{all.data?.filter(p => !due.data?.some(d => d.id === p.id)).slice(0, 5).map(prompt => <div key={prompt.id} className="row-item"><div><strong style={{ fontSize: 12 }}>{prompt.itemTitle || "Reading reflection"}</strong><span className="subtle" style={{ display: "block", marginTop: 4 }}>Due {readableDate(prompt.dueAt)}</span>{prompt.attempts?.some(attempt => attempt.response.trim()) && <details className="details"><summary>Past answers</summary>{prompt.attempts.filter(attempt => attempt.response.trim()).map(attempt => <p key={attempt.id} style={{ whiteSpace: "pre-wrap" }}>{attempt.response} <small className="subtle">· {readableDate(attempt.createdAt)}</small></p>)}</details>}</div></div>)}</div></aside></div>}</>;
}
