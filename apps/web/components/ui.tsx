"use client";
import Link from "next/link";
import { ArrowRight, Bookmark, BookOpen, Check, CircleHelp, RotateCcw, ThumbsDown, ThumbsUp, X } from "lucide-react";
import { api, json, publicationDate, safeExternal } from "@/lib/api";
import type { Item, ItemState } from "@/lib/types";
import { useEffect, useRef, useState } from "react";

export function Notice({ error, onRetry }: { error?: string | null; onRetry?: () => void }) { return error ? <div role="alert" className="error" style={{ marginBottom: 18 }}>{error} {onRetry && <button className="btn small" onClick={onRetry} style={{ marginLeft: 10 }}><RotateCcw size={13}/> Retry</button>}</div> : null; }
export function Empty({ icon = <BookOpen size={22}/>, title, body, action }: { icon?: React.ReactNode; title: string; body: string; action?: React.ReactNode }) { return <div className="empty"><div className="empty-icon">{icon}</div><h2>{title}</h2><p>{body}</p>{action}</div>; }
export function Loading() { return <div className="loading" role="status">Gathering what’s here…</div>; }
export function Kind({ kind }: { kind: string }) { return <span className="kind-label"><span>✳</span>{kind === "paper" ? "Research paper" : (kind === "book" || kind === "book-section") ? "Book chapter" : kind.replaceAll("-", " ")}</span>; }
export function SourceLine({ item }: { item: Item }) { return <span className="meta-line"><b>{item.sourceName || item.provider || "Personal import"}</b><span className="dot-sep">·</span><span>{publicationDate(item.publishedAt)}</span>{item.author && <><span className="dot-sep">·</span><span>{item.author}</span></>}</span>; }
export function StateActions({ item, onChange, compact = false }: { item: Item; onChange?: (item: Item) => void; compact?: boolean }) {
  const [current, setCurrent] = useState(item);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const inFlight = useRef(false);
  useEffect(() => { if (!inFlight.current) setCurrent(item); }, [item]);
  async function change(patch: Partial<ItemState>) {
    if (inFlight.current) return;
    inFlight.current = true;
    setPending(true);
    const prior = current;
    const next = { ...current, state: { ...current.state, ...patch } };
    setCurrent(next); setError(null);
    try {
      const state = await api<ItemState>(`/items/${item.id}/state`, json("PATCH", patch));
      const committed = { ...next, state: { ...next.state, ...state } };
      setCurrent(committed);
      // Notify parents only after persistence, so reloads cannot read the pre-mutation state.
      onChange?.(committed);
    }
    catch (cause) { setCurrent(prior); setError(cause instanceof Error ? cause.message : "Could not save change."); }
    finally { inFlight.current = false; setPending(false); }
  }
  return <span className="action-row" onClick={event => event.stopPropagation()}>
    {compact ? <><button disabled={pending} className={`icon-button ${current.state.saved ? "active" : ""}`} aria-label={current.state.saved ? "Remove from library" : "Save to library"} title={current.state.saved ? "Remove from library" : "Save to library"} onClick={() => void change({ saved: !current.state.saved })}><Bookmark size={17} fill={current.state.saved ? "currentColor" : "none"}/></button><button disabled={pending} className={`icon-button ${current.state.read ? "active" : ""}`} aria-label={current.state.read ? "Mark unread" : "Mark read"} title="Mark read" onClick={() => void change({ read: !current.state.read })}><Check size={16}/></button><button disabled={pending} className="icon-button" aria-label="Pass on this item" title="Pass on this item" onClick={() => void change({ dismissed: true })}><X size={16}/></button></>
      : <><button disabled={pending} className={`btn small ${current.state.saved ? "accent" : ""}`} onClick={() => void change({ saved: !current.state.saved })}><Bookmark size={14}/>{current.state.saved ? "Saved" : "Save"}</button><button disabled={pending} className="btn small" onClick={() => void change({ read: !current.state.read })}><Check size={14}/>{current.state.read ? "Read" : "Mark read"}</button><button disabled={pending} className={`btn small ${current.state.understood ? "accent" : ""}`} onClick={() => void change({ understood: !current.state.understood })}><Check size={14}/>{current.state.understood ? "Understood" : "I understand this"}</button><button disabled={pending} className={`btn small ${current.state.feedback === 1 ? "accent" : ""}`} onClick={() => void change({ feedback: current.state.feedback === 1 ? 0 : 1 })}><ThumbsUp size={14}/>More like this</button><button disabled={pending} className={`btn small ${current.state.feedback === -1 ? "accent" : ""}`} onClick={() => void change({ feedback: current.state.feedback === -1 ? 0 : -1 })}><ThumbsDown size={14}/>Less like this</button></>}
    {error && <span className="subtle" role="alert">{error}</span>}
  </span>;
}
export function ArticleCard({ item, onChange }: { item: Item; onChange?: (item: Item) => void }) { return <article className="panel article-card"><div className="card-top"><Kind kind={item.kind}/><span>{item.state.read ? "READ" : ""}</span></div><h3><Link href={`/items/${item.id}`}>{item.title}</Link></h3><p>{item.excerpt ? item.excerpt.slice(0, 240) + (item.excerpt.length > 240 ? "…" : "") : "Open to explore this source and its connections."}</p><div className="card-bottom"><SourceLine item={item}/><StateActions key={item.id} item={item} onChange={onChange} compact/></div>{item.reason && <details className="details"><summary><CircleHelp size={12} style={{ display: "inline", marginRight: 4 }}/>Why this?</summary><p>{item.reason}</p>{item.evidenceItemIds?.map((id, index) => <Link key={id} className="pill" style={{ marginRight: 6 }} href={`/items/${id}`}>Supporting item {index + 1} →</Link>)}</details>}</article>; }
export function ItemRow({ item, action }: { item: Item; action?: React.ReactNode }) { return <div className="row-item"><div><Kind kind={item.kind}/><h3><Link className="text-link" href={`/items/${item.id}`}>{item.title}</Link></h3><SourceLine item={item}/></div><div className="action-row">{action}<Link href={`/items/${item.id}`} className="btn small" aria-label={`Open ${item.title}`}><ArrowRight size={15}/></Link></div></div>; }
export function ExternalLink({ url, children, className = "btn" }: { url?: string | null; children: React.ReactNode; className?: string }) { const safe = safeExternal(url); return safe ? <a href={safe} target="_blank" rel="noopener noreferrer" className={className}>{children}</a> : null; }
