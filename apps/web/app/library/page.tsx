"use client";
import Link from "next/link";
import { useState } from "react";
import { ArrowRight, BookOpen, FileUp, Plus, Search } from "lucide-react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { Book, Item } from "@/lib/types";
import { Empty, ItemRow, Loading, Notice, StateActions } from "@/components/ui";
import { DocumentCard } from "@/components/document-card";

export default function LibraryPage() {
  const [tab, setTab] = useState<"saved" | "history" | "books" | "notes">("saved");
  const items = useApi<{ items: Item[]; total: number }>(tab === "saved" ? "/items?saved=true&limit=100" : tab === "notes" ? "/items?notes=true&limit=100" : "/items?opened=true&limit=100");
  const readItems = useApi<{ items: Item[]; total: number }>("/items?read=true&limit=100");
  const books = useApi<Book[]>("/library/books");
  const [filter, setFilter] = useState("");
  const [sort, setSort] = useState<"recent" | "title">("recent");
  const [loadingMore, setLoadingMore] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [uploadVersion, setUploadVersion] = useState(0);
  const [error, setError] = useState<string | null>(null);
  async function reloadDocuments() { await Promise.all([books.reload(), items.reload()]); }
  async function upload(file?: File) {
    if (!file || uploading) return;
    setUploading(true); setError(null);
    try {
      const form = new FormData(); form.append("file", file);
      await api("/library/books/upload", { method: "POST", body: form });
      await reloadDocuments(); setUploadVersion(version => version + 1);
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Could not import document."); }
    finally { setUploading(false); }
  }
  const all = tab === "history" ? [...new Map([...(items.data?.items || []), ...(readItems.data?.items || [])].map(x => [x.id, x])).values()] : items.data?.items || [];
  const shown = (tab === "saved" ? all.filter(x => x.state.saved) : tab === "history" ? all.filter(x => x.state.read || x.state.lastOpenedAt) : tab === "notes" ? all.filter(x => x.notes?.length) : [])
    .filter(x => (x.title + " " + (x.author || "") + " " + (x.excerpt || "")).toLowerCase().includes(filter.toLowerCase()))
    .sort((a, b) => sort === "title" ? a.title.localeCompare(b.title) : (b.state.lastOpenedAt || b.ingestedAt).localeCompare(a.state.lastOpenedAt || a.ingestedAt));
  const hasMore = (items.data?.items.length || 0) < (items.data?.total || 0) || tab === "history" && (readItems.data?.items.length || 0) < (readItems.data?.total || 0);
  async function more() {
    setLoadingMore(true); setError(null);
    try {
      const base = tab === "saved" ? "saved=true" : tab === "notes" ? "notes=true" : "opened=true";
      if (items.data && items.data.items.length < items.data.total) {
        const next = await api<{items: Item[]; total: number}>(`/items?${base}&limit=100&offset=${items.data.items.length}`);
        items.setData(old => old ? { items: [...old.items, ...next.items], total: next.total } : next);
      }
      if (tab === "history" && readItems.data && readItems.data.items.length < readItems.data.total) {
        const next = await api<{items: Item[]; total: number}>(`/items?read=true&limit=100&offset=${readItems.data.items.length}`);
        readItems.setData(old => old ? { items: [...old.items, ...next.items], total: next.total } : next);
      }
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Could not load more items."); }
    finally { setLoadingMore(false); }
  }
  return <><div className="page-heading"><div><span className="eyebrow accent">✳ THE THINGS YOU KEPT</span><h1>Your library</h1><p>A home for sources, private books, and thoughts you want to return to. Nothing disappears when a source refreshes.</p></div><Link className="btn" href="/search"><Plus size={15}/>Import a link</Link></div><Notice error={items.error || readItems.error || books.error || error} onRetry={() => { void items.reload(); void readItems.reload(); void books.reload(); }}/><div className="tab-row" role="tablist" aria-label="Library sections">{(["saved", "history", "books", "notes"] as const).map(value => <button key={value} role="tab" aria-selected={tab === value} className={`tab ${tab === value ? "active" : ""}`} onClick={() => setTab(value)}>{value === "saved" ? "Saved" : value === "history" ? "Reading history" : value === "books" ? "Books & files" : "Notes"}</button>)}</div>
    {tab !== "books" && <div className="action-row" style={{ marginBottom: 17 }}><label className="field" style={{ flex: "1 1 230px" }}>Filter this shelf<span style={{ position: "relative" }}><Search size={15} style={{ position: "absolute", left: 11, top: 13, color: "var(--muted)" }}/><input style={{ paddingLeft: 35 }} value={filter} onChange={event => setFilter(event.target.value)} placeholder="Title, author, or idea"/></span></label><label className="field">Sort<select value={sort} onChange={event => setSort(event.target.value as typeof sort)}><option value="recent">Recent</option><option value="title">Title</option></select></label></div>}
    {tab !== "books" ? items.loading && !items.data ? <Loading/> : shown.length === 0 ? <Empty title={tab === "saved" ? "No saved items yet" : tab === "notes" ? "No notes yet" : "A fresh page"} body={tab === "saved" ? "Save anything that feels worth returning to. Your sources remain here when you come back." : tab === "notes" ? "Open a source and write in the margins. Your thoughts will appear here." : "Items you open or mark read will show up in this history."} action={<Link className="btn primary" href="/">Go discover <ArrowRight size={14}/></Link>}/> : <div className="panel panel-pad">{shown.map(item => <div key={item.id}><ItemRow item={item} action={<StateActions item={item} onChange={() => { void items.reload(); void readItems.reload(); }} compact/>}/>{tab === "notes" && item.notes?.map(note => <p key={note.id} className="quote-box">{note.body === "Highlighted passage" ? note.quote : note.body}</p>)}</div>)}</div> : <div className="two-column"><div><h2 className="section-title">Your documents</h2>{books.loading && !books.data ? <Loading/> : !books.data?.length ? <Empty icon={<BookOpen size={22}/>} title="An empty bookshelf" body="Import a personal PDF or EPUB to read by section and carry its ideas into your trails."/> : <div className="stack">{books.data.map(book => <DocumentCard key={book.id} book={book} refreshVersion={uploadVersion} onChanged={reloadDocuments}/>)}</div>}</div><aside><div className="panel panel-pad stack"><FileUp color="var(--accent)" size={27}/><h2 className="section-title" style={{ margin: 0 }}>Bring a book in</h2><p className="muted" style={{ lineHeight: 1.6, margin: 0 }}>PDF and EPUB files stay in your local library. Extraction may vary by document; the original is always available.</p><label className="field">Choose a PDF or EPUB<input className="file-input" type="file" accept=".pdf,.epub,application/pdf,application/epub+zip" disabled={uploading} onChange={event => { void upload(event.target.files?.[0]); event.target.value = ""; }}/></label>{uploading && <span className="subtle" role="status">Importing and finding sections…</span>}</div></aside></div>}

    {tab !== "books" && hasMore && <div style={{ textAlign: "center", marginTop: 20 }}><button className="btn" disabled={loadingMore} onClick={more}>{loadingMore ? "Loading…" : "Load more"}</button></div>}
  </>;
}
