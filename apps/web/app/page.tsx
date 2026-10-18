"use client";
import Link from "next/link";
import { ArrowRight, ArrowUpRight, Compass, RefreshCw, Shuffle, Sparkles, X } from "lucide-react";
import { useMemo, useState } from "react";
import { api, json, readableDate } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { Edition, HoleSummary, Item, Topic } from "@/lib/types";
import { RecommendationExplanation } from "@/components/recommendation-explanation";
import { ArticleCard, Empty, Kind, Loading, Notice, SourceLine, StateActions } from "@/components/ui";

export default function DiscoverPage() {
  const edition = useApi<Edition>("/discover/today");
  const topics = useApi<Topic[]>("/topics");
  const holes = useApi<HoleSummary[]>("/holes");
  const [topic, setTopic] = useState("all");
  const [busy, setBusy] = useState<string | null>(null);
  const [surprise, setSurprise] = useState<Item | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const visible = useMemo(() => (edition.data?.items || []).filter(item => !item.state.dismissed && (topic === "all" || item.topics?.some(t => t.id === topic))), [edition.data, topic]);
  const feature = visible[0];
  const rest = visible.slice(1);
  const update = (item: Item) => edition.setData(old => old ? { ...old, items: old.items.map(x => x.id === item.id ? item : x) } : old);
  async function request(path: string, key: string) { setBusy(key); setActionError(null); try { edition.setData(await api<Edition>(path, json("POST"))); } catch (e) { setActionError(e instanceof Error ? e.message : "Could not update selection."); } finally { setBusy(null); } }
  async function getMore() { setBusy("more"); setActionError(null); try { edition.setData(await api<Edition>("/discover/more")); } catch (e) { setActionError(e instanceof Error ? e.message : "Could not load more."); } finally { setBusy(null); } }
  async function findSurprise() { setBusy("surprise"); setActionError(null); try { setSurprise(await api<Item>("/discover/surprise")); } catch (e) { setActionError(e instanceof Error ? e.message : "No surprise available yet."); } finally { setBusy(null); } }
  async function dismiss(item: Item) { const old = edition.data; update({ ...item, state: { ...item.state, dismissed: true } }); try { await api(`/items/${item.id}/state`, json("PATCH", { dismissed: true })); } catch (e) { edition.setData(old); setActionError(e instanceof Error ? e.message : "Could not dismiss."); } }
  return <>
    <div className="page-heading"><div><span className="eyebrow accent">✳ YOUR DAILY DETOUR</span><h1 className="hero-title">Follow what <em>pulls you in.</em></h1><p>A finite selection of real things to read, watch, and wonder about. Leave whenever you like; your place will be here.</p></div><div className="action-row"><button className="btn" onClick={findSurprise} disabled={!!busy}><Shuffle size={15}/>Surprise me</button><button className="btn" onClick={() => void request("/discover/refresh-edition", "refresh")} disabled={!!busy}><RefreshCw size={15}/>New selection</button></div></div>
    <Notice error={edition.error || actionError} onRetry={() => void edition.reload()}/>
    {!!holes.data?.length && <div className="panel panel-pad" style={{ marginBottom: 24 }}><span className="eyebrow">TRAILS TO PICK UP</span>{holes.data.slice(0, 2).map(hole => <div className="row-item" key={hole.id}><div><h3><Link href={`/holes/${hole.id}`}>{hole.title}</Link></h3><p>{hole.nodeCount || 1} stops · {hole.origin === "curated" || hole.origin === "example" ? "Curated starting trail" : hole.origin === "legacy-unknown" ? "Earlier saved trail" : "Your last position is saved."}</p></div><Link className="btn small" href={`/holes/${hole.id}`}>{hole.origin === "curated" || hole.origin === "example" ? "Explore" : "Continue"} <ArrowRight size={14}/></Link></div>)}</div>}
    {surprise && <div className="panel panel-pad" style={{ marginBottom: 24, background: "var(--lilac)" }}><div className="action-row" style={{ justifyContent: "space-between" }}><span className="eyebrow">A LITTLE DETOUR</span><button className="icon-button" onClick={() => setSurprise(null)} aria-label="Close surprise"><X size={16}/></button></div><h2 className="section-title" style={{ marginTop: 12 }}><Link href={`/items/${surprise.id}`}>{surprise.title}</Link></h2><SourceLine item={surprise}/><RecommendationExplanation item={surprise}/><div style={{ marginTop: 16 }}><Link className="btn primary" href={`/items/${surprise.id}`}>Follow this thread <ArrowRight size={14}/></Link></div></div>}
    {topics.data && topics.data.length > 0 && <div className="chip-row" style={{ marginBottom: 24 }}><button onClick={() => setTopic("all")} className={`pill ${topic === "all" ? "active" : ""}`}>All curiosities</button>{topics.data.filter(t => t.enabled).map(t => <button key={t.id} onClick={() => setTopic(t.id)} className={`pill ${topic === t.id ? "active" : ""}`}>{t.name}</button>)}</div>}
    {edition.loading && !edition.data ? <Loading/> : !feature ? <Empty icon={<Compass size={22}/>} title={edition.data?.items.length ? "Nothing in this topic today" : "Your trail starts with a source"} body={edition.data?.items.length ? "Choose another topic, ask for a new selection, or explore your whole library." : "Add a feed, import a link, or look for research. RabbitHole only shows material from real sources you choose."} action={<div className="action-row" style={{ justifyContent: "center" }}><Link className="btn primary" href="/settings">Add a source <ArrowRight size={15}/></Link><Link className="btn" href="/search">Explore research</Link></div>}/> : <>
      <div className="panel" style={{ display: "grid", gridTemplateColumns: "minmax(0,1fr)", overflow: "hidden", marginBottom: 26 }}><div style={{ padding: "clamp(25px,4vw,48px)", background: "linear-gradient(115deg,var(--paper),var(--accentsoft))" }}><div className="action-row" style={{ justifyContent: "space-between" }}><Kind kind={feature.kind}/><span className="eyebrow">01 / {visible.length.toString().padStart(2, "0")}</span></div><h2 className="serif" style={{ fontSize: "clamp(31px,4vw,49px)", lineHeight: 1.12, maxWidth: 750, margin: "25px 0 16px" }}><Link href={`/items/${feature.id}`}>{feature.title}</Link></h2><p style={{ color: "var(--muted)", lineHeight: 1.7, maxWidth: 640 }}>{feature.excerpt || "Open the original source and explore where it leads."}</p><SourceLine item={feature}/><div className="action-row" style={{ marginTop: 27 }}><Link className="btn primary" href={`/items/${feature.id}`}>Step inside <ArrowUpRight size={15}/></Link><StateActions item={feature} onChange={update} compact/><button className="btn ghost" onClick={() => void dismiss(feature)}><X size={14}/>Pass on this</button></div><RecommendationExplanation item={feature}/></div></div>
      <div className="page-heading"><div><span className="eyebrow">THE REST OF YOUR EDITION</span><h2 className="section-title" style={{ margin: "9px 0 0" }}>More to wander into</h2></div><span className="subtle">{edition.data?.items.length || 0} selected · {readableDate(edition.data?.day)}</span></div>
      {rest.length > 0 && <div className="article-grid">{rest.map(item => <ArticleCard key={item.id} item={item} onChange={update}/>)}</div>}
      <div style={{ textAlign: "center", marginTop: 30 }}><p className="subtle">You’ve reached the end of this selection.</p><button className="btn" onClick={() => void getMore()} disabled={!!busy}><Sparkles size={15}/>{busy === "more" ? "Looking…" : "Show me a little more"}</button></div>
    </>}
  </>;
}
