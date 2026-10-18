"use client";
import Link from "next/link";
import { useState } from "react";
import { ArrowRight, BookOpen, FileUp, Plus, Search } from "lucide-react";
import { api, json, readableDate } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { Book, BookSection, Item } from "@/lib/types";
import { Empty, ItemRow, Loading, Notice, StateActions } from "@/components/ui";

export default function LibraryPage() {
  const [tab, setTab] = useState<"saved" | "history" | "books" | "notes">("saved");
  const items = useApi<{ items: Item[]; total: number }>(tab === "saved" ? "/items?saved=true&limit=100" : tab === "notes" ? "/items?notes=true&limit=100" : "/items?opened=true&limit=100");
  const readItems = useApi<{ items: Item[]; total: number }>("/items?read=true&limit=100");
  const books = useApi<Book[]>("/library/books");
  const [filter, setFilter] = useState("");
  const [sort, setSort] = useState<"recent" | "title">("recent");
  const [loadingMore, setLoadingMore] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [retryingBook, setRetryingBook] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [sections, setSections] = useState<Record<string, BookSection[]>>({});
  const [editingBook, setEditingBook] = useState<string | null>(null);
  const [bookTitle, setBookTitle] = useState("");
  const [bookAuthor, setBookAuthor] = useState("");
  const [rangeTitle, setRangeTitle] = useState("");
  const [startPage, setStartPage] = useState(1);
  const [endPage, setEndPage] = useState(1);
  async function upload(file?: File) { if (!file) return; setUploading(true); setError(null); try { const form = new FormData(); form.append("file", file); await api("/library/books/upload", { method: "POST", body: form }); await books.reload(); await items.reload(); } catch (e) { setError(e instanceof Error ? e.message : "Could not import document."); } finally { setUploading(false); } }
  async function toggleBook(book: Book) { if (expanded === book.id) { setExpanded(null); return; } setExpanded(book.id); if (sections[book.id]) return; setError(null); try { const list = await api<BookSection[]>(`/library/books/${book.id}/sections`); setSections(old => ({ ...old, [book.id]: list })); } catch (e) { setError(e instanceof Error ? e.message : "Could not load sections."); } }
  async function saveBook(book: Book) { if (!bookTitle.trim()) return; setError(null); try { await api(`/library/books/${book.id}`, json("PATCH", { title: bookTitle.trim(), author: bookAuthor.trim() || null })); setEditingBook(null); await books.reload(); } catch (e) { setError(e instanceof Error ? e.message : "Could not save book details."); } }
  async function addRange(book: Book) { if (!rangeTitle.trim()) return; setError(null); try { await api(`/library/books/${book.id}/sections`, json("POST", { title: rangeTitle.trim(), startPage, endPage })); setRangeTitle(""); const list = await api<BookSection[]>(`/library/books/${book.id}/sections`); setSections(old => ({ ...old, [book.id]: list })); await items.reload(); } catch (e) { setError(e instanceof Error ? e.message : "Could not add range."); } }
  async function retryBook(book: Book) {
    setRetryingBook(book.id); setError(null);
    try {
      const result = await api<{ document: Book; sections: BookSection[] }>(`/library/books/${book.id}/retry`, json("POST"));
      setSections(old => ({ ...old, [book.id]: result.sections }));
      await Promise.all([books.reload(), items.reload()]);
    } catch (e) { setError(e instanceof Error ? e.message : "Could not retry document extraction."); await books.reload(); }
    finally { setRetryingBook(null); }
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
  return <><div className="page-heading"><div><span className="eyebrow accent">âœ³ THE THINGS YOU KEPT</span><h1>Your library</h1><p>A home for sources, private books, and thoughts you want to return to. Nothing disappears when a source refreshes.</p></div><Link className="btn" href="/search"><Plus size={15}/>Import a link</Link></div><Notice error={items.error || readItems.error || books.error || error} onRetry={() => { void items.reload(); void readItems.reload(); void books.reload(); }}/><div className="tab-row" role="tablist" aria-label="Library sections">{(["saved", "history", "books", "notes"] as const).map(value => <button key={value} role="tab" aria-selected={tab === value} className={`tab ${tab === value ? "active" : ""}`} onClick={() => setTab(value)}>{value === "saved" ? "Saved" : value === "history" ? "Reading history" : value === "books" ? "Books & files" : "Notes"}</button>)}</div>
    {tab !== "books" && <div className="action-row" style={{ marginBottom: 17 }}><label className="field" style={{ flex: "1 1 230px" }}>Filter this shelf<span style={{ position: "relative" }}><Search size={15} style={{ position: "absolute", left: 11, top: 13, color: "var(--muted)" }}/><input style={{ paddingLeft: 35 }} value={filter} onChange={event => setFilter(event.target.value)} placeholder="Title, author, or idea"/></span></label><label className="field">Sort<select value={sort} onChange={event => setSort(event.target.value as typeof sort)}><option value="recent">Recent</option><option value="title">Title</option></select></label></div>}
    {tab !== "books" ? items.loading && !items.data ? <Loading/> : shown.length === 0 ? <Empty title={tab === "saved" ? "No saved items yet" : tab === "notes" ? "No notes yet" : "A fresh page"} body={tab === "saved" ? "Save anything that feels worth returning to. Your sources remain here when you come back." : tab === "notes" ? "Open a source and write in the margins. Your thoughts will appear here." : "Items you open or mark read will show up in this history."} action={<Link className="btn primary" href="/">Go discover <ArrowRight size={14}/></Link>}/> : <div className="panel panel-pad">{shown.map(item => <div key={item.id}><ItemRow item={item} action={<StateActions item={item} onChange={() => { void items.reload(); void readItems.reload(); }} compact/>}/>{tab === "notes" && item.notes?.map(note => <p key={note.id} className="quote-box">{note.body === "Highlighted passage" ? note.quote : note.body}</p>)}</div>)}</div> : <div className="two-column"><div><h2 className="section-title">Your documents</h2>{books.loading && !books.data ? <Loading/> : !books.data?.length ? <Empty icon={<BookOpen size={22}/>} title="An empty bookshelf" body="Import a personal PDF or EPUB to read by section and carry its ideas into your trails."/> : <div className="stack">{books.data.map(book => <div key={book.id} className="panel panel-pad"><div className="row-item" style={{ paddingTop: 0 }}><div><span className="eyebrow">{book.fileName.split(".").pop()?.toUpperCase()} Â· {book.status}</span><h3 className="serif" style={{ fontSize: 23, margin: "9px 0 4px" }}>{book.title}</h3><span className="subtle">{book.author || "Author unknown"} Â· Added {readableDate(book.createdAt)}{book.pageCount ? ` Â· ${book.pageCount} pages` : ""}</span></div><button className="btn small" onClick={() => void toggleBook(book)}>{expanded === book.id ? "Close" : "Sections"} <ArrowRight size={13}/></button></div>{book.error && <p className="error">{book.error}</p>}{["parsing", "pending", "failed", "original-only"].includes(book.status) && <button className="btn small" disabled={retryingBook !== null} onClick={() => void retryBook(book)}>{retryingBook === book.id ? "Retrying extraction…" : "Retry extraction"}</button>}<button className="btn small" onClick={() => { setEditingBook(editingBook === book.id ? null : book.id); setBookTitle(book.title); setBookAuthor(book.author || ""); }}>Edit details</button>{editingBook === book.id && <div className="stack" style={{ marginTop: 12 }}><label className="field">Book title<input value={bookTitle} onChange={e => setBookTitle(e.target.value)}/></label><label className="field">Author<input value={bookAuthor} onChange={e => setBookAuthor(e.target.value)}/></label><button className="btn primary" onClick={() => void saveBook(book)}>Save book details</button></div>}{expanded === book.id && <div className="stack"><div>{sections[book.id]?.map(section => <div key={section.id} className="row-item"><div><h3>{section.title}</h3><span className="subtle">{section.startPage ? `Pages ${section.startPage}${section.endPage ? `â€“${section.endPage}` : ""}` : "Section"}</span></div><Link className="btn small" href={`/items/${section.itemId}`}>Read <ArrowRight size={13}/></Link></div>)}</div>{book.fileName.toLowerCase().endsWith(".pdf") && <div className="stack" style={{ borderTop: "1px solid var(--line)", paddingTop: 17 }}><span className="eyebrow">MARK A PAGE RANGE</span><div className="form-grid"><label className="field">Title<input value={rangeTitle} onChange={e => setRangeTitle(e.target.value)} placeholder="Chapter or section"/></label><div className="form-grid"><label className="field">From<input type="number" min="1" max={book.pageCount || undefined} value={startPage} onChange={e => setStartPage(Number(e.target.value))}/></label><label className="field">To<input type="number" min={startPage} max={book.pageCount || undefined} value={endPage} onChange={e => setEndPage(Number(e.target.value))}/></label></div></div><button className="btn small" disabled={!rangeTitle.trim()} onClick={() => void addRange(book)}><Plus size={13}/>Add range</button></div>}<Link href={`/api/library/books/${book.id}/file`} target="_blank" className="btn small">Open original file</Link></div>}</div>)}</div>}</div><aside><div className="panel panel-pad stack"><FileUp color="var(--accent)" size={27}/><h2 className="section-title" style={{ margin: 0 }}>Bring a book in</h2><p className="muted" style={{ lineHeight: 1.6, margin: 0 }}>PDF and EPUB files stay in your local library. Extraction may vary by document; the original is always available.</p><label className="field">Choose a PDF or EPUB<input className="file-input" type="file" accept=".pdf,.epub,application/pdf,application/epub+zip" disabled={uploading} onChange={e => void upload(e.target.files?.[0])}/></label>{uploading && <span className="subtle" role="status">Importing and finding sectionsâ€¦</span>}</div></aside></div>}
    {tab !== "books" && hasMore && <div style={{ textAlign: "center", marginTop: 20 }}><button className="btn" disabled={loadingMore} onClick={more}>{loadingMore ? "Loading…" : "Load more"}</button></div>}
  </>;
}
