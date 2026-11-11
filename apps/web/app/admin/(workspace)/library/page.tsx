"use client";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useEffect, useRef, useState } from "react";
import { ArrowRight, BookOpen, FileUp, Plus, Search } from "lucide-react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { Book, Item } from "@/lib/types";
import { Empty, ItemRow, Loading, Notice, StateActions } from "@/components/ui";
import { DocumentCard } from "@/components/document-card";
import { PublicationControl } from "@/components/publication-control";

export default function LibraryPage() {
  return <Suspense fallback={<Loading/>}><LibraryRoute/></Suspense>;
}

function LibraryRoute() {
  const sourceId = useSearchParams().get("sourceId") || "";
  return <LibraryContent key={sourceId} sourceId={sourceId}/>;
}

function LibraryContent({ sourceId }: { sourceId: string }) {
  const [tab, setTab] = useState<"all" | "saved" | "history" | "books" | "notes">(sourceId ? "all" : "saved");
  const [visibility, setVisibility] = useState("all");
  const source = useApi<{ id: string; name: string }>(sourceId ? `/sources/${encodeURIComponent(sourceId)}` : null);
  const sourceQuery = sourceId ? `&sourceId=${encodeURIComponent(sourceId)}` : "";
  const visibilityQuery = sourceQuery + (visibility === "all" ? "" : `&isPublic=${visibility === "public"}`);
  const itemQuery = tab === "all" ? "" : tab === "saved" ? "saved=true&" : tab === "notes" ? "notes=true&" : "opened=true&";
  const items = useApi<{ items: Item[]; total: number }>(tab !== "books" ? `/items?${itemQuery}limit=100${visibilityQuery}` : null);
  const readItems = useApi<{ items: Item[]; total: number }>(tab === "history" ? `/items?read=true&limit=100${visibilityQuery}` : null);
  const books = useApi<Book[]>(tab === "books" ? "/library/books" : null);
  const [filter, setFilter] = useState("");
  const [sort, setSort] = useState<"recent" | "title">("recent");
  const [loadingMore, setLoadingMore] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [uploadVersion, setUploadVersion] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const paginationRequest = useRef<AbortController | null>(null);
  useEffect(() => {
    paginationRequest.current?.abort();
    paginationRequest.current = null;
    setLoadingMore(false); setError(null);
    return () => paginationRequest.current?.abort();
  }, [tab, visibilityQuery]);
  async function reloadItems() {
    paginationRequest.current?.abort();
    paginationRequest.current = null;
    setLoadingMore(false); setError(null);
    await Promise.all([items.reload(), readItems.reload()]);
  }
  async function reloadDocuments() { await Promise.all([books.reload(), reloadItems()]); }
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
  const shown = (tab === "saved" ? all.filter(x => x.state.saved) : tab === "history" ? all.filter(x => x.state.read || x.state.lastOpenedAt) : tab === "notes" ? all.filter(x => x.notes?.length) : tab === "all" ? all : [])
    .filter(x => (x.title + " " + (x.author || "") + " " + (x.excerpt || "")).toLowerCase().includes(filter.toLowerCase()))
    .sort((a, b) => sort === "title" ? a.title.localeCompare(b.title) : (b.state.lastOpenedAt || b.ingestedAt).localeCompare(a.state.lastOpenedAt || a.ingestedAt));
  const hasMore = (items.data?.items.length || 0) < (items.data?.total || 0) || tab === "history" && (readItems.data?.items.length || 0) < (readItems.data?.total || 0);
  const refreshingItems = items.loading || tab === "history" && readItems.loading;
  async function more() {
    if (loadingMore || refreshingItems || paginationRequest.current) return;
    const controller = new AbortController();
    paginationRequest.current = controller;
    setLoadingMore(true); setError(null);
    try {
      const base = itemQuery;
      if (items.data && items.data.items.length < items.data.total) {
        const next = await api<{items: Item[]; total: number}>(`/items?${base}limit=100&offset=${items.data.items.length}${visibilityQuery}`, { signal: controller.signal });
        if (!controller.signal.aborted) items.setData(old => old ? { items: [...old.items, ...next.items], total: next.total } : next);
      }
      if (tab === "history" && readItems.data && readItems.data.items.length < readItems.data.total) {
        const next = await api<{items: Item[]; total: number}>(`/items?read=true&limit=100&offset=${readItems.data.items.length}${visibilityQuery}`, { signal: controller.signal });
        if (!controller.signal.aborted) readItems.setData(old => old ? { items: [...old.items, ...next.items], total: next.total } : next);
      }
    } catch (cause) { if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : "Could not load more items."); }
    finally { if (!controller.signal.aborted) { paginationRequest.current = null; setLoadingMore(false); } }
  }
  return <><div className="page-heading"><div><h1>{sourceId ? source.data?.name || "Source library" : "Your library"}</h1><p>{sourceId ? "All content imported from this source. Use the tabs to narrow the collection." : "Saved items, books, notes, and reading history."}</p></div><div className="action-row">{sourceId && <Link className="btn" href="/admin/library">All sources</Link>}<Link className="btn" href="/admin/search"><Plus size={15}/>Import a link</Link></div></div><Notice error={source.error || items.error || readItems.error || books.error || error} onRetry={() => { void source.reload(); void reloadItems(); void books.reload(); }}/><div className="tab-row" role="tablist" aria-label="Library sections">{(["all", "saved", "history", "books", "notes"] as const).filter(value => !sourceId || value !== "books").map(value => <button key={value} role="tab" aria-selected={tab === value} className={`tab ${tab === value ? "active" : ""}`} onClick={() => setTab(value)}>{value === "all" ? "All items" : value === "saved" ? "Saved" : value === "history" ? "Reading history" : value === "books" ? "Books & files" : "Notes"}</button>)}</div>
    {sourceId && items.data && (tab === "saved" || tab === "all") && <p className="subtle" role="status">{items.data.total} {tab === "saved" ? "saved" : "imported"} item{items.data.total === 1 ? "" : "s"}{visibility !== "all" ? ` · ${visibility}` : ""}</p>}
    {tab !== "books" && <div className="action-row" style={{ marginBottom: 17 }}><label className="field" style={{ flex: "1 1 230px" }}>Filter items<span style={{ position: "relative" }}><Search size={15} style={{ position: "absolute", left: 11, top: 13, color: "var(--muted)" }}/><input style={{ paddingLeft: 35 }} value={filter} onChange={event => setFilter(event.target.value)} placeholder="Title, author, or idea"/></span></label><label className="field">Visibility<select value={visibility} onChange={event => setVisibility(event.target.value)}><option value="all">All visibility</option><option value="public">Public</option><option value="private">Private</option></select></label><label className="field">Sort<select value={sort} onChange={event => setSort(event.target.value as typeof sort)}><option value="recent">Recent</option><option value="title">Title</option></select></label></div>}
    {tab !== "books" ? (items.loading && !items.data || tab === "history" && readItems.loading && !readItems.data) ? <Loading/> : (items.error && !items.data || tab === "history" && readItems.error && !readItems.data) ? null : shown.length === 0 ? <Empty title={filter ? "No matching items" : sourceId ? tab === "saved" ? "No saved items from this source" : tab === "notes" ? "No notes from this source" : tab === "history" ? "No reading history from this source" : "No items from this source" : tab === "saved" ? "No saved items yet" : tab === "notes" ? "No notes yet" : tab === "all" ? "No imported items yet" : "No reading history yet"} body={filter ? "Try a different title, author, or phrase, or clear the filter." : sourceId ? visibility !== "all" ? "Try All visibility to include every item from this source." : tab === "saved" ? "Choose All items to browse this source, then save anything you want to return to." : "Items you import, read, or add notes to from this source will appear in the matching section." : tab === "saved" ? "Use Save on any item to find it here later." : tab === "notes" ? "Open an item and add a note. You can find your notes here." : "Items you open or mark as read appear here."} action={sourceId && tab === "saved" ? <button className="btn primary" onClick={() => setTab("all")}>Browse all items <ArrowRight size={14}/></button> : <Link className="btn primary" href="/admin">Browse Discover <ArrowRight size={14}/></Link>}/> : <div className="panel panel-pad">{shown.map(item => <div key={item.id}><ItemRow item={item} action={<StateActions item={item} onChange={() => void reloadItems()} compact/>}/><PublicationControl item={item} onChange={() => void reloadItems()}/>{tab === "notes" && item.notes?.map(note => <p key={note.id} className="quote-box">{note.body === "Highlighted passage" ? note.quote : note.body}</p>)}</div>)}</div> : <div className="two-column"><div><h2 className="section-title">Your documents</h2>{books.loading && !books.data ? <Loading/> : !books.data?.length ? <Empty icon={<BookOpen size={22}/>} title="No books or files yet" body="Upload a PDF or EPUB to read it here."/> : <div className="stack">{books.data.map(book => <DocumentCard key={book.id} book={book} refreshVersion={uploadVersion} onChanged={reloadDocuments}/>)}</div>}</div><aside><div className="panel panel-pad stack"><FileUp color="var(--accent)" size={27}/><h2 className="section-title" style={{ margin: 0 }}>Upload a book or document</h2><p className="muted" style={{ lineHeight: 1.6, margin: 0 }}>PDF and EPUB files stay in your private library. If the text can’t be extracted, you can open the original file.</p><label className="field">Choose a PDF or EPUB<input className="file-input" type="file" accept=".pdf,.epub,application/pdf,application/epub+zip" disabled={uploading} onChange={event => { void upload(event.target.files?.[0]); event.target.value = ""; }}/></label>{uploading && <span className="subtle" role="status">Importing document…</span>}</div></aside></div>}

    {tab !== "books" && hasMore && <div style={{ textAlign: "center", marginTop: 20 }}><button className="btn" disabled={loadingMore || refreshingItems} onClick={more}>{loadingMore ? "Loading…" : "Load more"}</button></div>}
  </>;
}
