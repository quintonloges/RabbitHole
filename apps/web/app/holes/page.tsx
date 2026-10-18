"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowRight, GitBranch, Plus } from "lucide-react";
import { api, json, readableDate } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { HoleSummary } from "@/lib/types";
import { Empty, Loading, Notice } from "@/components/ui";
import { useState } from "react";

export default function HolesPage() {
  const holes = useApi<HoleSummary[]>("/holes");
  const router = useRouter();
  const [question, setQuestion] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function create() { if (!question.trim()) return; setBusy(true); setError(null); try { const hole = await api<HoleSummary>("/holes", json("POST", { question: question.trim(), title: question.trim() })); router.push(`/holes/${hole.id}`); } catch (e) { setError(e instanceof Error ? e.message : "Could not start a trail."); } finally { setBusy(false); } }
  return <><div className="page-heading"><div><span className="eyebrow accent">✳ FOLLOW THE THREAD</span><h1>Rabbit holes</h1><p>Every interesting turn has a place here. Branch freely, retrace your steps, and pick up a thought where you left it.</p></div></div><Notice error={holes.error || error} onRetry={() => void holes.reload()}/><div className="two-column"><div><h2 className="section-title">Saved trails</h2>{holes.loading && !holes.data ? <Loading/> : !holes.data?.length ? <Empty icon={<GitBranch size={22}/>} title="No trails yet" body="Start from a discovered item, or begin with a question you want to chase." action={<Link className="btn primary" href="/">Discover something <ArrowRight size={14}/></Link>}/> : <div className="stack">{holes.data.map(hole => <Link key={hole.id} href={`/holes/${hole.id}`} className="panel panel-pad" style={{ display: "flex", justifyContent: "space-between", gap: 18, alignItems: "center" }}><div><span className="eyebrow">{hole.origin === "curated" ? "CURATED TRAIL · " : hole.origin === "example" ? "EXAMPLE TRAIL · " : hole.origin === "legacy-unknown" ? "EARLIER TRAIL · " : ""}{hole.nodeCount ?? 0} TURN{hole.nodeCount === 1 ? "" : "S"} · UPDATED {readableDate(hole.updatedAt)}</span><h3 className="serif" style={{ fontSize: 24, margin: "9px 0 4px" }}>{hole.title || hole.rootTitle || "Untitled trail"}</h3><span className="subtle">{hole.rootTitle && hole.rootTitle !== hole.title ? `Began with ${hole.rootTitle}` : "Pick up where you left off"}</span></div><ArrowRight size={17}/></Link>)}</div>}</div><aside><div className="callout"><span className="eyebrow">A BLANK PATH</span><h2>Start with a question</h2><p>Write down an idea or puzzle. You can attach real sources and branch from it as you explore.</p><label className="field" style={{ marginTop: 18 }}>Your question<input value={question} onChange={e => setQuestion(e.target.value)} onKeyDown={e => { if (e.key === "Enter") void create(); }} placeholder="Why do flood myths recur?"/></label><button className="btn primary" style={{ marginTop: 12 }} onClick={create} disabled={busy || !question.trim()}><Plus size={15}/>Begin a trail</button></div></aside></div></>;
}
