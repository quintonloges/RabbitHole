"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { ArrowRight, Compass, RefreshCw, Shuffle, Sparkles, X } from "lucide-react";
import { Suspense, useEffect, useRef, useState } from "react";
import { api, json, readableDate } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { Edition, HoleSummary, Item, Topic } from "@/lib/types";
import { RecommendationExplanation } from "@/components/recommendation-explanation";
import { ArticleList } from "@/components/article-list";
import { Empty, Loading, Notice, SourceLine } from "@/components/ui";

type CollectionPage = { items: Item[]; total: number };
const pageSize = 30;

export default function DiscoverPage() {
  return <Suspense fallback={<Loading/>}><DiscoverFeed/></Suspense>;
}

function DiscoverFeed() {
  const router = useRouter();
  const params = useSearchParams();
  const topicId = params.get("topic") || "";
  const requestedPage = Number(params.get("page") || "1");
  const page = Number.isInteger(requestedPage) ? Math.max(1, Math.min(3333, requestedPage)) : 1;
  const offset = (page - 1) * pageSize;
  const topics = useApi<Topic[]>("/topics");
  const selectedTopic = topics.data?.find(topic => topic.id === topicId);
  const edition = useApi<Edition>(topicId ? null : "/discover/today");
  const initializingEdition = useRef(false);
  const { data: editionData, setData: setEdition, setError: setEditionError } = edition;
  useEffect(() => {
    if (topicId || !editionData || editionData.id || initializingEdition.current) return;
    initializingEdition.current = true;
    void api<Edition>("/discover/today", json("POST")).then(setEdition).catch(cause => setEditionError(cause instanceof Error ? cause.message : "Could not prepare today’s selection.")).finally(() => { initializingEdition.current = false; });
  }, [topicId, editionData, setEdition, setEditionError]);
  const collection = useApi<CollectionPage>(selectedTopic ? `/items?topicId=${encodeURIComponent(selectedTopic.id)}&dismissed=false&order=reading&limit=${pageSize}&offset=${offset}` : null);
  const holes = useApi<HoleSummary[]>("/holes");
  const [busy, setBusy] = useState<string | null>(null);
  const [surprise, setSurprise] = useState<Item | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const visible = (topicId ? collection.data?.items || [] : edition.data?.items || []).filter(item => !item.state.dismissed);
  const loading = topicId ? topics.loading || collection.loading : edition.loading;
  const total = topicId ? collection.data?.total || 0 : visible.length;
  const trails = holes.data?.slice(0, 2) || [];
  const invalidTopic = Boolean(topicId && topics.data && !selectedTopic);
  const activeError = topicId ? topics.error || collection.error : edition.error;

  function browseTopic(id: string) {
    setSurprise(null); setActionError(null);
    router.push(id ? `/admin?topic=${encodeURIComponent(id)}` : "/admin", { scroll: false });
  }
  function update(item: Item) {
    edition.setData(old => old ? { ...old, items: old.items.map(x => x.id === item.id ? item : x) } : old);
    collection.setData(old => old ? { ...old, items: old.items.map(x => x.id === item.id ? item : x) } : old);
    if (topicId && item.state.dismissed) void collection.reload();
  }
  async function newSelection() {
    setBusy("refresh"); setActionError(null);
    try { edition.setData(await api<Edition>("/discover/refresh-edition", json("POST"))); }
    catch (e) { setActionError(e instanceof Error ? e.message : "Could not update selection."); }
    finally { setBusy(null); }
  }
  async function getMore() {
    setBusy("more"); setActionError(null);
    try { edition.setData(await api<Edition>("/discover/more", json("POST"))); }
    catch (e) { setActionError(e instanceof Error ? e.message : "Could not load more."); }
    finally { setBusy(null); }
  }
  async function findSurprise() {
    setBusy("surprise"); setActionError(null);
    try { setSurprise(await api<Item>("/discover/surprise")); }
    catch (e) { setActionError(e instanceof Error ? e.message : "No surprise available yet."); }
    finally { setBusy(null); }
  }
  function retry() {
    setActionError(null);
    if (topicId) { void topics.reload(); void collection.reload(); }
    else void edition.reload();
  }
  const topicPageUrl = (next: number) => `/admin?topic=${encodeURIComponent(topicId)}${next > 1 ? `&page=${next}` : ""}`;

  return <>
    <div className="page-heading discover-heading">
      <div><h1 className="hero-title">Discover</h1><p>Browse today’s picks or choose a topic.</p></div>
      <div className="action-row"><button className="btn" onClick={findSurprise} disabled={!!busy}><Shuffle size={15}/>Surprise me</button>{!topicId && <button className="btn" onClick={newSelection} disabled={!!busy}><RefreshCw size={15}/>New selection</button>}</div>
    </div>
    {surprise && <div className="panel panel-pad" style={{ marginBottom: 24, background: "var(--lilac)" }}><div className="action-row" style={{ justifyContent: "space-between" }}><span className="subtle">Something different</span><button className="icon-button" onClick={() => setSurprise(null)} aria-label="Close surprise"><X size={16}/></button></div><h2 className="section-title" style={{ marginTop: 12 }}><Link href={`/admin/items/${surprise.id}`}>{surprise.title}</Link></h2><SourceLine item={surprise}/><RecommendationExplanation item={surprise}/><div style={{ marginTop: 16 }}><Link className="btn primary" href={`/admin/items/${surprise.id}`}>Open item <ArrowRight size={14}/></Link></div></div>}
    <div className="discover-toolbar">
      <button className={`btn daily-picks${!topicId ? " accent" : ""}`} aria-pressed={!topicId} onClick={() => browseTopic("")}>Today’s picks</button>
      <label className="topic-picker">Browse topic<select value={topicId} onChange={event => browseTopic(event.target.value)} disabled={!topics.data}>
        <option value="">Choose a topic…</option>
        {invalidTopic && <option value={topicId}>Unavailable topic</option>}
        {topics.data?.filter(topic => topic.enabled || topic.id === topicId).map(topic => <option key={topic.id} value={topic.id}>{topic.name}</option>)}
      </select></label>
      <span className="browse-scope">{topicId ? "All items in this topic" : "Picked from your collection"}</span>
    </div>
    <Notice error={activeError || actionError} onRetry={retry}/>
    {!topicId && topics.error && <Notice error={topics.error} onRetry={() => void topics.reload()}/>}
    <div className={`discover-layout${trails.length ? " has-trails" : ""}`}>
      <section className="discover-feed" aria-label={topicId ? "Topic discoveries" : "Daily discoveries"} aria-busy={loading}>
        <div className="edition-heading"><h2>{topicId ? selectedTopic?.name || "Browse topic" : "Today’s selection"}</h2><span role="status">{loading ? "Loading items…" : topicId ? `${total} item${total === 1 ? "" : "s"} · Newest first · books in reading order` : `${total} selected · ${readableDate(edition.data?.day)}`}</span></div>
        {loading ? <Loading/> : invalidTopic ? <Empty title="This topic is no longer available" body="Choose another topic or return to today’s picks." action={<button className="btn primary" onClick={() => browseTopic("")}>Today’s picks</button>}/>
          : !visible.length && !activeError ? <Empty icon={<Compass size={22}/>} title={topicId ? total ? "No items on this page" : "No items in this topic yet" : edition.data?.items.length ? "No picks left to show" : "Add your first source"}
            body={topicId ? "Try another topic or return to today’s picks." : edition.data?.items.length ? "Choose another topic or use New selection to refresh your picks." : "Add a feed in Settings, import a link, or search for papers."}
            action={<div className="action-row" style={{ justifyContent: "center" }}>{topicId ? <><button className="btn primary" onClick={() => browseTopic("")}>Today’s picks</button>{page > 1 && <Link className="btn" href={topicPageUrl(1)}>First page</Link>}</> : <><Link className="btn primary" href="/admin/settings">Add a source <ArrowRight size={15}/></Link><Link className="btn" href="/admin/search">Search research</Link></>}</div>}/>
          : visible.length > 0 && <ArticleList items={visible} onChange={update} label={topicId ? `${selectedTopic?.name} items` : "Today's selection"} start={topicId ? offset + 1 : 1}/>}
        {!loading && !activeError && visible.length > 0 && (topicId ? <nav className="topic-pagination" aria-label="Topic pages">
          {page > 1 ? <Link className="btn small" href={topicPageUrl(page - 1)}>Previous</Link> : <span/>}
          <span className="subtle">{offset + 1}–{Math.min(offset + visible.length, total)} of {total}</span>
          {offset + pageSize < total ? <Link className="btn small" href={topicPageUrl(page + 1)}>Next</Link> : <span/>}
        </nav> : <div className="edition-end"><p className="subtle">That’s all for today’s picks.</p><button className="btn" onClick={getMore} disabled={!!busy}><Sparkles size={15}/>{busy === "more" ? "Looking…" : "Show more"}</button></div>)}
      </section>
      {trails.length > 0 && <aside className="discover-rail" aria-label="Saved trails"><h2>Saved trails</h2>{trails.map(hole => <div className="trail-resume" key={hole.id}><h3><Link href={`/admin/holes/${hole.id}`}>{hole.title}</Link></h3><p>{hole.nodeCount || 1} stops · {hole.origin === "curated" || hole.origin === "example" ? "Suggested trail" : hole.origin === "legacy-unknown" ? "Earlier trail" : "Saved trail"}</p><Link className="text-link" href={`/admin/holes/${hole.id}`}>{hole.origin === "curated" || hole.origin === "example" ? "Explore" : "Continue"} <ArrowRight size={13}/></Link></div>)}<Link className="all-trails text-link" href="/admin/holes">All rabbit holes <ArrowRight size={13}/></Link></aside>}
    </div>
  </>;
}
