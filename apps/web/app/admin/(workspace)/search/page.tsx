"use client";

import Link from "next/link";
import { useState } from "react";
import { ArrowRight, ArrowUpRight, Compass, Link2, Plus, Search as SearchIcon } from "lucide-react";
import { api, json, publicationDate, safeExternal } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { Item, Source, Topic } from "@/lib/types";
import { Empty, ItemRow, Notice } from "@/components/ui";

type Research = { provider: string; providerId: string; title: string; author?: string | null; canonicalUrl?: string; publishedAt?: string | null; excerpt?: string | null };
type LocalPage = { items: Item[]; total: number };
type Filters = { kind: string; sourceId: string; topicId: string; saved: string; read: string; publishedAfter: string; publishedBefore: string };
const emptyFilters: Filters = { kind: "", sourceId: "", topicId: "", saved: "", read: "", publishedAfter: "", publishedBefore: "" };
const pageSize = 30;

export default function SearchPage() {
  const sources = useApi<Source[]>("/sources");
  const topics = useApi<Topic[]>("/topics");
  const [q, setQ] = useState("");
  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [local, setLocal] = useState<LocalPage | null>(null);
  const [localPage, setLocalPage] = useState(0);
  const [localSearched, setLocalSearched] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const [research, setResearch] = useState<Research[]>([]);
  const [researchSearched, setResearchSearched] = useState(false);
  const [researchError, setResearchError] = useState<string | null>(null);
  const [provider, setProvider] = useState("openalex");
  const [url, setUrl] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const [importError, setImportError] = useState<string | null>(null);
  const [warning, setWarning] = useState<string | null>(null);
  const [imported, setImported] = useState<Item | null>(null);
  const hasFilters = Object.values(filters).some(Boolean);
  const canSearchLocal = Boolean(q.trim() || hasFilters);

  function patchFilter(key: keyof Filters, value: string) { setFilters(old => ({ ...old, [key]: value })); setLocalSearched(false); }
  async function searchLocal(page = 0) {
    if (!canSearchLocal) return;
    setBusy("local"); setLocalError(null);
    const params = new URLSearchParams({ q: q.trim(), limit: String(pageSize), offset: String(page * pageSize) });
    for (const [key, value] of Object.entries(filters)) if (value) params.set(key, value);
    try {
      const result = await api<LocalPage>(`/search?${params.toString()}`);
      setLocal(result); setLocalPage(page); setLocalSearched(true);
    } catch (cause) { setLocalError(cause instanceof Error ? cause.message : "Your collection search is unavailable."); }
    finally { setBusy(null); }
  }
  async function searchResearch() {
    if (!q.trim()) return;
    setBusy("research"); setResearchError(null); setWarning(null);
    try {
      const result = await api<{ items: Research[]; warning?: string }>(`/research/search?provider=${provider}&q=${encodeURIComponent(q.trim())}`);
      setResearch(result.items); setResearchSearched(true); setWarning(result.warning || null);
    } catch (cause) { setResearchError(cause instanceof Error ? cause.message : "Research search is unavailable."); }
    finally { setBusy(null); }
  }
  async function importUrl() {
    if (!url.trim()) return;
    setBusy("url"); setImportError(null);
    try { const item = await api<Item>("/items/import-url", json("POST", { url: url.trim() })); setImported(item); setUrl(""); }
    catch (cause) { setImportError(cause instanceof Error ? cause.message : "Could not import URL."); }
    finally { setBusy(null); }
  }
  async function importResearch(result: Research) {
    setBusy(result.providerId); setImportError(null);
    try { const item = await api<Item>("/research/import", json("POST", { provider: result.provider || provider, providerId: result.providerId })); setImported(item); }
    catch (cause) { setImportError(cause instanceof Error ? cause.message : "Could not import paper."); }
    finally { setBusy(null); }
  }

  return <>
    <div className="page-heading"><div><h1>Search</h1><p>Search your collection, find papers, or import a link.</p></div></div>
    <Notice error={importError}/>
    {imported && <div className="success" style={{ marginBottom: 22 }}>Added “{imported.title}” to your collection. <Link href={`/admin/items/${imported.id}`} style={{ textDecoration: "underline" }}>Open item →</Link></div>}
    <div className="two-column"><div className="stack">
      <div className="panel panel-pad"><label className="field" style={{ marginTop: 12 }}>Search terms<input value={q} onChange={event => { setQ(event.target.value); setLocalSearched(false); setResearchSearched(false); }} onKeyDown={event => { if (event.key === "Enter") void searchLocal(); }} placeholder="consciousness, architecture, a lost civilization…"/></label>
        <div className="action-row" style={{ marginTop: 13 }}><button className="btn primary" disabled={!canSearchLocal || !!busy} onClick={() => void searchLocal()}><SearchIcon size={15}/>Search your collection</button><button className="btn" disabled={!q.trim() || !!busy} onClick={searchResearch}>Search research <ArrowUpRight size={14}/></button></div>
        <details className="details" style={{ marginTop: 15 }}><summary>Filter your collection{hasFilters ? " · active" : ""}</summary><div className="form-grid" style={{ marginTop: 15 }}>
          <label className="field">Type<select value={filters.kind} onChange={event => patchFilter("kind", event.target.value)}><option value="">All types</option>{["article", "paper", "book", "video", "audio", "music"].map(kind => <option key={kind} value={kind}>{kind[0].toUpperCase() + kind.slice(1)}</option>)}</select></label>
          <label className="field">Source<select value={filters.sourceId} onChange={event => patchFilter("sourceId", event.target.value)}><option value="">All sources</option>{sources.data?.map(source => <option key={source.id} value={source.id}>{source.name}</option>)}</select></label>
          <label className="field">Topic<select value={filters.topicId} onChange={event => patchFilter("topicId", event.target.value)}><option value="">All topics</option>{topics.data?.map(topic => <option key={topic.id} value={topic.id}>{topic.name}</option>)}</select></label>
          <label className="field">Saved<select value={filters.saved} onChange={event => patchFilter("saved", event.target.value)}><option value="">Any</option><option value="true">Saved</option><option value="false">Not saved</option></select></label>
          <label className="field">Read<select value={filters.read} onChange={event => patchFilter("read", event.target.value)}><option value="">Any</option><option value="true">Read</option><option value="false">Unread</option></select></label>
          <label className="field">Published from<input type="date" value={filters.publishedAfter} max={filters.publishedBefore || undefined} onChange={event => patchFilter("publishedAfter", event.target.value)}/></label>
          <label className="field">Published through<input type="date" value={filters.publishedBefore} min={filters.publishedAfter || undefined} onChange={event => patchFilter("publishedBefore", event.target.value)}/></label>
        </div><div className="action-row" style={{ marginTop: 14 }}><button className="btn small" onClick={() => { setFilters(emptyFilters); setLocalSearched(false); }} disabled={!hasFilters}>Clear filters</button><span className="subtle">Date filters hide items with an unknown publication date.</span></div></details>
        <div className="action-row" style={{ marginTop: 13 }}><label className="field" style={{ display: "flex", alignItems: "center", gap: 8 }}>Research provider<select style={{ width: "auto" }} value={provider} onChange={event => setProvider(event.target.value)}><option value="openalex">OpenAlex</option><option value="arxiv">arXiv</option></select></label></div>
      </div>
      {busy === "local" && <p className="subtle" role="status">Searching your collection…</p>}
      <Notice error={localError} onRetry={() => void searchLocal(localPage)}/>
      {localSearched && !localError && (local?.items.length ? <div className="panel panel-pad"><div className="action-row" style={{ justifyContent: "space-between" }}><h2 className="section-title" style={{ margin: 0 }}>From your collection</h2><span className="subtle">{local.total} match{local.total === 1 ? "" : "es"}</span></div>{local.items.map(item => <ItemRow key={item.id} item={item}/>)}<div className="action-row" style={{ justifyContent: "space-between", marginTop: 18 }}><button className="btn small" disabled={localPage === 0 || !!busy} onClick={() => void searchLocal(localPage - 1)}>Previous</button><span className="subtle">Page {localPage + 1} of {Math.ceil(local.total / pageSize)}</span><button className="btn small" disabled={(localPage + 1) * pageSize >= local.total || !!busy} onClick={() => void searchLocal(localPage + 1)}>Next</button></div></div> : <Empty icon={<Compass size={22}/>} title="No matches in your collection" body="Try different search terms, clear a filter, or search for research papers."/>)}
      {busy === "research" && <p className="subtle" role="status">Searching research…</p>}
      <Notice error={researchError} onRetry={() => void searchResearch()}/>{warning && <p className="error">{warning}</p>}
      {researchSearched && !researchError && (research.length ? <div className="panel panel-pad"><h2 className="section-title">Research results</h2><p className="subtle">Import a paper to add it to your collection.</p>{research.map(result => <div key={`${result.provider}-${result.providerId}`} className="row-item"><div><span className="eyebrow">{result.provider || provider} · {publicationDate(result.publishedAt)}</span><h3>{result.title}</h3><p>{result.author || "Author unknown"}{result.excerpt ? ` · ${result.excerpt.slice(0, 160)}` : ""}</p></div><div className="action-row"><button className="btn small" disabled={!!busy} onClick={() => void importResearch(result)}><Plus size={13}/>Import</button>{safeExternal(result.canonicalUrl) && <a className="btn small" href={safeExternal(result.canonicalUrl)} target="_blank" rel="noopener noreferrer" aria-label="Open original research"><ArrowUpRight size={14}/></a>}</div></div>)}</div> : <Empty icon={<Compass size={22}/>} title="No research results" body="Try different search terms or switch providers."/>)}
    </div><aside><div className="panel panel-pad"><Link2 size={26} color="var(--accent)"/><h2 className="section-title" style={{ marginTop: 12 }}>Import a link</h2><p className="muted" style={{ lineHeight: 1.6 }}>Paste a public link to an article, paper, video, or track. Some sites only share a title and link.</p><label className="field">Public URL<input type="url" value={url} onChange={event => setUrl(event.target.value)} placeholder="https://…"/></label><button className="btn primary" style={{ marginTop: 13 }} disabled={!url.trim() || !!busy} onClick={importUrl}>Import link <ArrowRight size={14}/></button></div></aside></div>
  </>;
}
