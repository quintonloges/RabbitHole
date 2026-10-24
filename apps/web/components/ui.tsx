"use client";
import Link from "next/link";
import { ArrowRight, Bookmark, BookOpen, Check, RotateCcw, ThumbsDown, ThumbsUp, X } from "lucide-react";
import { api, json, publicationDate, safeExternal } from "@/lib/api";
import type { Item, ItemState } from "@/lib/types";
import { contentType, itemHeading, sourceLabel } from "@/lib/item-display";
import { ItemByline, ItemExcerpt, ItemFormat } from "./item-summary";
import { useEffect, useRef, useState } from "react";

export function Notice({ error, onRetry }: { error?: string | null; onRetry?: () => void }) { return error ? <div role="alert" className="error" style={{ marginBottom: 18 }}>{error} {onRetry && <button className="btn small" onClick={onRetry} style={{ marginLeft: 10 }}><RotateCcw size={13}/> Retry</button>}</div> : null; }
export function Empty({ icon = <BookOpen size={22}/>, title, body, action }: { icon?: React.ReactNode; title: string; body: string; action?: React.ReactNode }) { return <div className="empty"><div className="empty-icon">{icon}</div><h2>{title}</h2><p>{body}</p>{action}</div>; }
export function Loading() { return <div className="loading" role="status">Loading…</div>; }
export function Kind({ kind, item }: { kind: string; item?: Item }) { return <span className="kind-label"><span>✳</span>{item ? contentType(item) : kind === "paper" ? "Research paper" : kind === "book-section" ? "Book section" : kind.replaceAll("-", " ")}</span>; }
export function SourceLine({ item }: { item: Item }) { return <span className="meta-line"><b>{sourceLabel(item)}</b>{!item.document && <><span className="dot-sep">·</span><span>{publicationDate(item.publishedAt)}</span></>}{item.author && <><span className="dot-sep">·</span><span>{item.author}</span></>}</span>; }
export function SourceDomain({ item }: { item: Item }) {
  const url = safeExternal(item.canonicalUrl);
  return url ? <a className="source-domain" href={url} target="_blank" rel="noopener noreferrer" aria-label={`Original source: ${item.title}`}>({new URL(url).hostname.replace(/^www\./, "")})</a> : null;
}
export function StateActions({ item, onChange, compact = false, inline = false, reader = false }: { item: Item; onChange?: (item: Item) => void; compact?: boolean; inline?: boolean; reader?: boolean }) {
  const [current, setCurrent] = useState(item);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const inFlight = useRef(false);
  const moreActions = useRef<HTMLDetailsElement>(null);
  useEffect(() => { if (!inFlight.current) setCurrent(item); }, [item]);
  useEffect(() => {
    if (!reader) return;
    function dismiss(event: PointerEvent | KeyboardEvent) {
      const menu = moreActions.current;
      if (!menu?.open) return;
      if (event instanceof KeyboardEvent) {
        if (event.key === "Escape") { menu.open = false; menu.querySelector("summary")?.focus(); }
      } else if (event.target instanceof Node && !menu.contains(event.target)) menu.open = false;
    }
    document.addEventListener("pointerdown", dismiss);
    document.addEventListener("keydown", dismiss);
    return () => { document.removeEventListener("pointerdown", dismiss); document.removeEventListener("keydown", dismiss); };
  }, [reader]);
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
  const reflection = <><button disabled={pending} className={`btn small ${current.state.understood ? "accent" : ""}`} onClick={() => void change({ understood: !current.state.understood })}><Check size={14}/>{current.state.understood ? "Understood" : "I understand this"}</button><button disabled={pending} className={`btn small ${current.state.feedback === 1 ? "accent" : ""}`} onClick={() => void change({ feedback: current.state.feedback === 1 ? 0 : 1 })}><ThumbsUp size={14}/>More like this</button><button disabled={pending} className={`btn small ${current.state.feedback === -1 ? "accent" : ""}`} onClick={() => void change({ feedback: current.state.feedback === -1 ? 0 : -1 })}><ThumbsDown size={14}/>Less like this</button></>;
  return <div className={`action-row${inline ? " inline-state-actions" : ""}`} onClick={event => event.stopPropagation()}>
    {inline ? <><button disabled={pending} className={`story-action ${current.state.saved ? "active" : ""}`} aria-label={current.state.saved ? "Remove from library" : "Save to library"} onClick={() => void change({ saved: !current.state.saved })}>{current.state.saved ? "Saved" : "Save"}</button><button disabled={pending} className={`story-action ${current.state.read ? "active" : ""}`} aria-label={current.state.read ? "Mark unread" : "Mark read"} onClick={() => void change({ read: !current.state.read })}>{current.state.read ? "Mark unread" : "Mark read"}</button><button disabled={pending} className="story-action" aria-label="Hide from discovery" onClick={() => void change({ dismissed: true })}>Hide</button></>
      : compact ? <><button disabled={pending} className={`icon-button ${current.state.saved ? "active" : ""}`} aria-label={current.state.saved ? "Remove from library" : "Save to library"} title={current.state.saved ? "Remove from library" : "Save to library"} onClick={() => void change({ saved: !current.state.saved })}><Bookmark size={17} fill={current.state.saved ? "currentColor" : "none"}/></button><button disabled={pending} className={`icon-button ${current.state.read ? "active" : ""}`} aria-label={current.state.read ? "Mark unread" : "Mark read"} title={current.state.read ? "Mark unread" : "Mark read"} onClick={() => void change({ read: !current.state.read })}><Check size={16}/></button><button disabled={pending} className="icon-button" aria-label="Hide from discovery" title="Hide from discovery" onClick={() => void change({ dismissed: true })}><X size={16}/></button></>
      : <><button disabled={pending} className={`btn small ${current.state.saved ? "accent" : ""}`} onClick={() => void change({ saved: !current.state.saved })}><Bookmark size={14}/>{current.state.saved ? "Saved" : "Save"}</button><button disabled={pending} className="btn small" onClick={() => void change({ read: !current.state.read })}><Check size={14}/>{current.state.read ? "Read" : "Mark read"}</button>{reader ? <details ref={moreActions} className="reader-more-actions"><summary>More</summary><div className="action-row">{reflection}</div></details> : reflection}</>}
    {error && <span className="subtle" role="alert">{error}</span>}
  </div>;
}
export function ItemRow({ item, action }: { item: Item; action?: React.ReactNode }) { return <div className="row-item collection-row"><div><div className="story-heading"><h3><Link className="text-link" href={`/admin/items/${item.id}`}>{itemHeading(item)}</Link></h3><ItemFormat item={item}/></div><ItemByline item={item}/><ItemExcerpt item={item}/></div><div className="action-row">{action}<Link href={`/admin/items/${item.id}`} className="btn small" aria-label={`Open ${item.title}`}><ArrowRight size={15}/></Link></div></div>; }
export function ExternalLink({ url, children, className = "btn" }: { url?: string | null; children: React.ReactNode; className?: string }) { const safe = safeExternal(url); return safe ? <a href={safe} target="_blank" rel="noopener noreferrer" className={className}>{children}</a> : null; }
